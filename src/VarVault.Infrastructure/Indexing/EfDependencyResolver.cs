using Microsoft.EntityFrameworkCore;
using VarVault.Common.Diagnostics;
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = Telemetry.StartActivity("index.resolve");

        // Page packages into a compact family map — never track full Package entities for the whole catalog.
        var familyMap = new Dictionary<string, List<AvailableVersion>>(StringComparer.Ordinal);
        var packageFamily = new Dictionary<long, string>();
        const int pageSize = 2_000;
        long lastId = 0;
        while (true)
        {
            var page = await db.Packages.AsNoTracking()
                .Where(p => p.Id > lastId)
                .OrderBy(p => p.Id)
                .Take(pageSize)
                .Select(p => new { p.Id, p.Creator, p.PackageName, p.VersionSort })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (page.Count == 0)
                break;
            foreach (var p in page)
            {
                var family = IdentityFold.Compute($"{p.Creator}.{p.PackageName}");
                packageFamily[p.Id] = family;
                if (!familyMap.TryGetValue(family, out var list))
                    familyMap[family] = list = [];
                list.Add(new AvailableVersion(p.VersionSort, p.Id));
            }
            lastId = page[^1].Id;
        }

        var varFilePackage = new Dictionary<long, long>();
        lastId = 0;
        while (true)
        {
            var page = await db.VarFiles.AsNoTracking()
                .Where(v => v.PackageId != null && v.Id > lastId)
                .OrderBy(v => v.Id)
                .Take(pageSize)
                .Select(v => new { v.Id, PackageId = v.PackageId!.Value })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (page.Count == 0)
                break;
            foreach (var v in page)
                varFilePackage[v.Id] = v.PackageId;
            lastId = page[^1].Id;
        }

        var aliasMap = await db.VarAliases.AsNoTracking()
            .Where(a => a.ResolvedPackageId != null)
            .Select(a => new { a.MissingRefKey, PackageId = a.ResolvedPackageId!.Value })
            .ToDictionaryAsync(a => a.MissingRefKey, a => a.PackageId, cancellationToken).ConfigureAwait(false);

        var reverseSources = new Dictionary<long, HashSet<long>>();
        var missing = 0;
        var resolved = 0;
        lastId = 0;
        while (true)
        {
            var deps = await db.Dependencies
                .Where(d => d.Id > lastId)
                .OrderBy(d => d.Id)
                .Take(pageSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (deps.Count == 0)
                break;

            foreach (var dep in deps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long? containerPackage = varFilePackage.TryGetValue(dep.VarFileId, out var cp) ? cp : null;
                var containerFamily = containerPackage is { } cpid && packageFamily.TryGetValue(cpid, out var cf) ? cf : null;
                ResolveOne(dep, containerPackage, containerFamily, familyMap, aliasMap);
                if (dep.IsMissing)
                    missing++;
                else
                {
                    resolved++;
                    if (dep.ResolvedPackageId is { } target && containerPackage is { } source && target != source)
                    {
                        if (!reverseSources.TryGetValue(target, out var set))
                            reverseSources[target] = set = [];
                        set.Add(source);
                    }
                }
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            lastId = deps[^1].Id;
        }

        // Reverse counts in pages — update via ExecuteUpdate to avoid tracking every Package.
        lastId = 0;
        var foundational = 0;
        while (true)
        {
            var page = await db.Packages.AsNoTracking()
                .Where(p => p.Id > lastId)
                .OrderBy(p => p.Id)
                .Take(pageSize)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (page.Count == 0)
                break;
            foreach (var id in page)
            {
                var count = reverseSources.TryGetValue(id, out var set) ? set.Count : 0;
                var isFoundational = count >= FoundationalThreshold;
                if (isFoundational)
                    foundational++;
                await db.Packages.Where(p => p.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.ReverseDependentCount, count)
                        .SetProperty(p => p.IsFoundational, isFoundational), cancellationToken)
                    .ConfigureAwait(false);
            }
            lastId = page[^1];
        }

        await UpdateHasMissingDepsPagedAsync(cancellationToken).ConfigureAwait(false);
        Telemetry.IndexResolveDurationMs.Record(sw.Elapsed.TotalMilliseconds);
        return new DependencyResolutionResult(resolved, missing, foundational);
    }

    public async Task<int> ResolveFamilyAsync(string creator, string packageName, CancellationToken cancellationToken = default)
    {
        var targetFamily = IdentityFold.Compute($"{creator}.{packageName}");

        var familyMap = new Dictionary<string, List<AvailableVersion>>(StringComparer.Ordinal);
        var packageFamily = new Dictionary<long, string>();
        var packages = await db.Packages.AsNoTracking()
            .Select(p => new { p.Id, p.Creator, p.PackageName, p.VersionSort })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var p in packages)
        {
            var family = IdentityFold.Compute($"{p.Creator}.{p.PackageName}");
            packageFamily[p.Id] = family;
            if (!familyMap.TryGetValue(family, out var list))
                familyMap[family] = list = [];
            list.Add(new AvailableVersion(p.VersionSort, p.Id));
        }

        var varFilePackage = await db.VarFiles.AsNoTracking()
            .Where(v => v.PackageId != null)
            .Select(v => new { v.Id, PackageId = v.PackageId!.Value })
            .ToDictionaryAsync(v => v.Id, v => v.PackageId, cancellationToken).ConfigureAwait(false);
        var aliasMap = await db.VarAliases.AsNoTracking()
            .Where(a => a.ResolvedPackageId != null)
            .Select(a => new { a.MissingRefKey, PackageId = a.ResolvedPackageId!.Value })
            .ToDictionaryAsync(a => a.MissingRefKey, a => a.PackageId, cancellationToken).ConfigureAwait(false);

        // SQL-filter to edges whose folded key starts with the family (exact family match via parse).
        var touched = 0;
        const int pageSize = 1_000;
        long lastId = 0;
        while (true)
        {
            var deps = await db.Dependencies
                .Where(d => d.Id > lastId)
                .OrderBy(d => d.Id)
                .Take(pageSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (deps.Count == 0)
                break;

            foreach (var dep in deps)
            {
                var parsed = DependencyRef.Parse(dep.DependsOnRefRaw);
                if (parsed.IsFailure || !string.Equals(parsed.Value.FamilyKey, targetFamily, StringComparison.Ordinal))
                    continue;

                long? containerPackage = varFilePackage.TryGetValue(dep.VarFileId, out var cp) ? cp : null;
                var containerFamily = containerPackage is { } cpid && packageFamily.TryGetValue(cpid, out var cf) ? cf : null;
                ResolveOne(dep, containerPackage, containerFamily, familyMap, aliasMap);
                touched++;
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            lastId = deps[^1].Id;
        }

        return touched;
    }

    public async Task<int> ResolveReferenceAsync(string requestedRef, CancellationToken cancellationToken = default)
    {
        var key = IdentityFold.Compute(requestedRef);
        var aliasTarget = await db.VarAliases.AsNoTracking()
            .Where(a => a.MissingRefKey == key && a.ResolvedPackageId != null)
            .Select(a => a.ResolvedPackageId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var dependencies = await db.Dependencies
            .Where(d => d.DependsOnRefKey == key &&
                        (d.IsMissing || d.ResolvedVia == ResolvedVia.Alias))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var previousTargets = dependencies
            .Where(d => d.ResolvedPackageId is not null)
            .Select(d => d.ResolvedPackageId!.Value)
            .Distinct()
            .ToList();
        var sourceVarFileIds = dependencies.Select(d => d.VarFileId).Distinct().ToList();
        foreach (var dependency in dependencies)
        {
            dependency.ResolvedPackageId = aliasTarget;
            dependency.IsMissing = aliasTarget is null;
            dependency.IsVersionSubstituted = false;
            dependency.ResolvedVia = aliasTarget is null ? ResolvedVia.None : ResolvedVia.Alias;
        }

        var saveDependencies = await db.SaveDependencies
            .Where(d => d.DependsOnRefKey == key &&
                        (aliasTarget == null || d.IsMissing || d.ResolvedPackageId == aliasTarget))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var dependency in saveDependencies)
        {
            dependency.ResolvedPackageId = aliasTarget;
            dependency.IsMissing = aliasTarget is null;
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (sourceVarFileIds.Count > 0)
        {
            var sourcePackageIds = await db.VarFiles.AsNoTracking()
                .Where(v => sourceVarFileIds.Contains(v.Id) && v.PackageId != null)
                .Select(v => v.PackageId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var packageId in sourcePackageIds)
            {
                var canonicalId = await db.Packages.AsNoTracking()
                    .Where(p => p.Id == packageId)
                    .Select(p => p.CanonicalVarFileId)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                var hasMissing = canonicalId is { } varFileId &&
                    await db.Dependencies.AsNoTracking()
                        .AnyAsync(d => d.VarFileId == varFileId && d.IsMissing, cancellationToken)
                        .ConfigureAwait(false);
                await db.PackageListItems.Where(i => i.PackageId == packageId)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.HasMissingDeps, hasMissing), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var affectedTargets = previousTargets
            .Select(id => (long?)id)
            .Append(aliasTarget)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        foreach (var targetId in affectedTargets)
        {
            var reverseCount = await (
                from d in db.Dependencies.AsNoTracking()
                join v in db.VarFiles.AsNoTracking() on d.VarFileId equals v.Id
                where d.ResolvedPackageId == targetId && v.PackageId != null && v.PackageId != targetId
                select v.PackageId!.Value)
                .Distinct()
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);
            await db.Packages.Where(p => p.Id == targetId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.ReverseDependentCount, reverseCount)
                    .SetProperty(p => p.IsFoundational, reverseCount >= FoundationalThreshold), cancellationToken)
                .ConfigureAwait(false);
        }

        return dependencies.Count + saveDependencies.Count;
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

    private async Task UpdateHasMissingDepsPagedAsync(CancellationToken cancellationToken)
    {
        const int pageSize = 2_000;
        long lastId = 0;
        while (true)
        {
            var page = await db.Packages.AsNoTracking()
                .Where(p => p.CanonicalVarFileId != null && p.Id > lastId)
                .OrderBy(p => p.Id)
                .Take(pageSize)
                .Select(p => new { p.Id, VarFileId = p.CanonicalVarFileId!.Value })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (page.Count == 0)
                break;

            var varIds = page.Select(p => p.VarFileId).ToList();
            var missingVars = await db.Dependencies.AsNoTracking()
                .Where(d => varIds.Contains(d.VarFileId) && d.IsMissing)
                .Select(d => d.VarFileId)
                .Distinct()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var missingSet = missingVars.ToHashSet();

            foreach (var pkg in page)
            {
                var hasMissing = missingSet.Contains(pkg.VarFileId);
                await db.PackageListItems.Where(i => i.PackageId == pkg.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.HasMissingDeps, hasMissing), cancellationToken)
                    .ConfigureAwait(false);
            }
            lastId = page[^1].Id;
        }
    }
}
