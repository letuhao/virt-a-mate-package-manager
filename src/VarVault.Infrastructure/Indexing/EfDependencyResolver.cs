using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// EF implementation of <see cref="IDependencyResolver"/>: a single catalog-wide resolution pass.
/// (Data-arch §5.3; checklist 2.2/2.5/2.8/2.9/2.11/2.15.)
/// </summary>
public sealed class EfDependencyResolver(VarVaultDbContext db) : IDependencyResolver
{
    /// <summary>In-degree at or above which a package is flagged foundational (impact-query short-circuit).</summary>
    public const int FoundationalThreshold = 25;

    public async Task<DependencyResolutionResult> ResolveAllAsync(CancellationToken cancellationToken = default)
    {
        // Family map: folded Creator.Package → its available versions.
        var packages = await db.Packages
            .Select(p => new { p.Id, p.Creator, p.PackageName, p.VersionSort })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var familyMap = new Dictionary<string, List<AvailableVersion>>(StringComparer.Ordinal);
        var packageFamily = new Dictionary<long, string>();
        foreach (var p in packages)
        {
            var family = IdentityFold.Compute($"{p.Creator}.{p.PackageName}");
            packageFamily[p.Id] = family;
            if (!familyMap.TryGetValue(family, out var list))
                familyMap[family] = list = [];
            list.Add(new AvailableVersion(p.VersionSort, p.Id));
        }

        var varFilePackage = await db.VarFiles
            .Where(v => v.PackageId != null)
            .Select(v => new { v.Id, PackageId = v.PackageId!.Value })
            .ToDictionaryAsync(v => v.Id, v => v.PackageId, cancellationToken).ConfigureAwait(false);

        var aliasMap = await db.VarAliases
            .Where(a => a.ResolvedPackageId != null)
            .Select(a => new { a.MissingRefKey, PackageId = a.ResolvedPackageId!.Value })
            .ToDictionaryAsync(a => a.MissingRefKey, a => a.PackageId, cancellationToken).ConfigureAwait(false);

        var dependencies = await db.Dependencies.ToListAsync(cancellationToken).ConfigureAwait(false);

        // reverseSources[target] = distinct source packages that depend on it.
        var reverseSources = new Dictionary<long, HashSet<long>>();
        var missing = 0;

        foreach (var dep in dependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long? containerPackage = varFilePackage.TryGetValue(dep.VarFileId, out var cp) ? cp : null;
            var containerFamily = containerPackage is { } cpid && packageFamily.TryGetValue(cpid, out var cf) ? cf : null;

            ResolveOne(dep, containerPackage, containerFamily, familyMap, aliasMap);

            if (dep.IsMissing)
                missing++;
            else if (dep.ResolvedPackageId is { } target && containerPackage is { } source && target != source)
            {
                if (!reverseSources.TryGetValue(target, out var set))
                    reverseSources[target] = set = [];
                set.Add(source);
            }
        }

        // Reverse-dependent counts + foundational flag (2.11).
        var foundational = 0;
        var trackedPackages = await db.Packages.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var pkg in trackedPackages)
        {
            var count = reverseSources.TryGetValue(pkg.Id, out var set) ? set.Count : 0;
            pkg.ReverseDependentCount = count;
            pkg.IsFoundational = count >= FoundationalThreshold;
            if (pkg.IsFoundational)
                foundational++;
        }

        // HasMissingDeps materialized bit — direct deps of each package's canonical var (2.15).
        await UpdateHasMissingDepsAsync(dependencies, cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new DependencyResolutionResult(dependencies.Count - missing, missing, foundational);
    }

    private static void ResolveOne(
        Dependency dep,
        long? containerPackage,
        string? containerFamily,
        Dictionary<string, List<AvailableVersion>> familyMap,
        Dictionary<string, long> aliasMap)
    {
        var parsed = DependencyRef.Parse(dep.DependsOnRefRaw, containerFamily);
        if (parsed.IsFailure)
        {
            // Unparseable — recorded, never dropped. (2.3)
            dep.IsMissing = true;
            dep.ResolvedPackageId = null;
            dep.IsVersionSubstituted = false;
            dep.ResolvedVia = ResolvedVia.None;
            return;
        }

        var reference = parsed.Value;

        // SELF → container package, never missing. (2.2)
        if (reference.IsSelf && containerPackage is { } self)
        {
            dep.IsMissing = false;
            dep.ResolvedPackageId = self;
            dep.IsVersionSubstituted = false;
            dep.ResolvedVia = ResolvedVia.Exact;
            dep.RefKind = RefKind.Self;
            return;
        }

        // Real match first (outranks alias). (2.9)
        var available = familyMap.GetValueOrDefault(reference.FamilyKey) ?? [];
        var resolution = VersionResolver.Resolve(reference, available);
        if (resolution is not null)
        {
            dep.IsMissing = false;
            dep.ResolvedPackageId = resolution.PackageId;
            dep.IsVersionSubstituted = resolution.IsVersionSubstituted;
            dep.ResolvedVia = resolution.Via;
            return;
        }

        // Fall back to an alias only when no real match exists.
        if (aliasMap.TryGetValue(dep.DependsOnRefKey, out var aliasTarget))
        {
            dep.IsMissing = false;
            dep.ResolvedPackageId = aliasTarget;
            dep.IsVersionSubstituted = false;
            dep.ResolvedVia = ResolvedVia.Alias;
            return;
        }

        dep.IsMissing = true;
        dep.ResolvedPackageId = null;
        dep.IsVersionSubstituted = false;
        dep.ResolvedVia = ResolvedVia.None;
    }

    private async Task UpdateHasMissingDepsAsync(List<Dependency> dependencies, CancellationToken cancellationToken)
    {
        // Canonical var → package.
        var canonical = await db.Packages
            .Where(p => p.CanonicalVarFileId != null)
            .Select(p => new { p.Id, VarFileId = p.CanonicalVarFileId!.Value })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var canonicalVarToPackage = canonical.ToDictionary(c => c.VarFileId, c => c.Id);

        var missingByPackage = new HashSet<long>();
        foreach (var dep in dependencies)
        {
            if (dep.IsMissing && canonicalVarToPackage.TryGetValue(dep.VarFileId, out var pkgId))
                missingByPackage.Add(pkgId);
        }

        var listItems = await db.PackageListItems.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in listItems)
            item.HasMissingDeps = missingByPackage.Contains(item.PackageId);
    }
}
