using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// Legacy Installed Packages / <c>MissingDepends</c>: analyse dependencies of packages active on the
/// current profile (<see cref="PackageListItem.IsActive"/>), resolve via <see cref="VersionResolver"/>
/// (exact / latest / closest) with alias fallback, and activate found names into the active loading preset.
/// </summary>
public sealed class EfInstalledDepsRepair(
    VarVaultDbContext db,
    IPresetService presets,
    IActivationService activation,
    IProfileService profiles)
    : IInstalledDepsRepair
{
    private readonly ActivePresetActivationHelper _activator = new(db, presets, activation, profiles);

    public async Task<InstalledDepsAnalysis> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        var activePackageIds = await db.PackageListItems.AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.PackageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (activePackageIds.Count == 0)
            return new InstalledDepsAnalysis([], 0);

        var canonicalVarFileIds = await db.Packages.AsNoTracking()
            .Where(p => activePackageIds.Contains(p.Id) && p.CanonicalVarFileId != null)
            .Select(p => p.CanonicalVarFileId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (canonicalVarFileIds.Count == 0)
            return new InstalledDepsAnalysis([], activePackageIds.Count);

        // Needed-by = distinct source packages (matches Missing deps screen semantics).
        var grouped = await (
                from d in db.Dependencies.AsNoTracking()
                join v in db.VarFiles.AsNoTracking() on d.VarFileId equals v.Id
                where canonicalVarFileIds.Contains(d.VarFileId) && d.RefKind != RefKind.Self && v.PackageId != null
                group v.PackageId!.Value by d.DependsOnRefRaw into g
                select new { Ref = g.Key, Count = g.Distinct().Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var allVersions = await db.Packages.AsNoTracking()
            .Select(p => new { p.Id, p.VarName, p.VersionSort, p.Creator, p.PackageName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var familyMap = new Dictionary<string, List<AvailableVersion>>(StringComparer.Ordinal);
        var idToName = new Dictionary<long, string>();
        foreach (var p in allVersions)
        {
            idToName[p.Id] = p.VarName;
            var familyKey = IdentityFold.Compute($"{p.Creator}.{p.PackageName}");
            if (!familyMap.TryGetValue(familyKey, out var list))
            {
                list = [];
                familyMap[familyKey] = list;
            }
            list.Add(new AvailableVersion(p.VersionSort, p.Id));
        }

        // Global aliases only — same map the catalog resolver uses when no real match exists.
        var aliasMap = await db.VarAliases.AsNoTracking()
            .Where(a => a.Scope == AliasScope.Global && a.ResolvedPackageId != null)
            .Select(a => new { a.MissingRefKey, PackageId = a.ResolvedPackageId!.Value })
            .ToDictionaryAsync(a => a.MissingRefKey, a => a.PackageId, cancellationToken)
            .ConfigureAwait(false);

        var entries = new List<InstalledDepsEntry>(grouped.Count);
        foreach (var row in grouped.OrderByDescending(g => g.Count).ThenBy(g => g.Ref, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = DependencyRef.Parse(row.Ref);
            if (parsed.IsFailure)
            {
                entries.Add(Missing(row.Ref, row.Count));
                continue;
            }

            var reference = parsed.Value;
            var available = familyMap.GetValueOrDefault(reference.FamilyKey) ?? [];
            var resolution = VersionResolver.Resolve(reference, available);
            if (resolution is not null)
            {
                var via = MapVia(resolution.Via);
                var varName = idToName.GetValueOrDefault(resolution.PackageId);
                if (string.IsNullOrWhiteSpace(varName))
                {
                    entries.Add(Missing(row.Ref, row.Count));
                    continue;
                }

                entries.Add(new InstalledDepsEntry(
                    row.Ref,
                    InLibrary: true,
                    ResolvedVarName: varName,
                    via,
                    row.Count,
                    NeedsAlias: via == InstalledDepsResolveVia.Closest));
                continue;
            }

            // Alias fallback (outranked by real match — same as EfDependencyResolver).
            var refKey = IdentityFold.Compute(row.Ref);
            if (aliasMap.TryGetValue(refKey, out var aliasTargetId)
                && idToName.TryGetValue(aliasTargetId, out var aliasName)
                && !string.IsNullOrWhiteSpace(aliasName))
            {
                entries.Add(new InstalledDepsEntry(
                    row.Ref,
                    InLibrary: true,
                    ResolvedVarName: aliasName,
                    InstalledDepsResolveVia.Alias,
                    row.Count,
                    NeedsAlias: false));
                continue;
            }

            entries.Add(Missing(row.Ref, row.Count));
        }

        return new InstalledDepsAnalysis(entries, activePackageIds.Count);
    }

    public async Task<MissingLogActivation> ActivateFromAnalysisAsync(
        InstalledDepsAnalysis analysis,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(analysis);

        var alreadyActive = (await db.PackageListItems.AsNoTracking()
                .Where(p => p.IsActive)
                .Join(db.Packages.AsNoTracking(), i => i.PackageId, p => p.Id, (_, p) => p.VarName)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Exact/Latest/Closest substitutes that aren't already linked — skip Alias (materialized via MissingVarLink on rebuild).
        var toActivate = analysis.Entries
            .Where(e => e.InLibrary
                        && e.Via is not InstalledDepsResolveVia.Alias
                        && !string.IsNullOrWhiteSpace(e.ResolvedVarName)
                        && !alreadyActive.Contains(e.ResolvedVarName!))
            .Select(e => e.ResolvedVarName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (toActivate.Count > 0)
            return await _activator.ActivateAsync(toActivate, cancellationToken).ConfigureAwait(false);

        // Alias-only hits still need a profile rebuild so ___MissingVarLink___ stays in sync.
        if (analysis.Entries.Any(e => e.Via == InstalledDepsResolveVia.Alias))
            return await _activator.RebuildActiveAsync(cancellationToken).ConfigureAwait(false);

        return new MissingLogActivation(0, 0, 0, 0);
    }

    public Task<MissingLogActivation> ActivateFoundAsync(
        IReadOnlyList<string> varNames,
        CancellationToken cancellationToken = default) =>
        _activator.ActivateAsync(varNames, cancellationToken);

    private static InstalledDepsEntry Missing(string @ref, int count) =>
        new(@ref, InLibrary: false, ResolvedVarName: null, InstalledDepsResolveVia.None, count, NeedsAlias: true);

    private static InstalledDepsResolveVia MapVia(ResolvedVia via) => via switch
    {
        ResolvedVia.Exact => InstalledDepsResolveVia.Exact,
        ResolvedVia.Latest => InstalledDepsResolveVia.Latest,
        ResolvedVia.Closest => InstalledDepsResolveVia.Closest,
        ResolvedVia.Alias => InstalledDepsResolveVia.Alias,
        _ => InstalledDepsResolveVia.None,
    };
}
