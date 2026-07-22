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
/// Installed-packages dependency repair: BFS from active packages through every resolvable dep
/// (exact / latest / closest / alias), surface true leftovers for alias Resolve, and activate found
/// names into the active loading preset. Deeper than legacy <c>MissingDepends</c> (one hop) — matches
/// legacy <c>VarsDependencies</c> recursive closure used by other install paths, plus activation's
/// forward-closure materialization.
/// </summary>
public sealed class EfInstalledDepsRepair(
    VarVaultDbContext db,
    IPresetService presets,
    IActivationService activation,
    IProfileService profiles)
    : IInstalledDepsRepair
{
    private const int FrontierChunkSize = 500;
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

        var allVersions = await db.Packages.AsNoTracking()
            .Select(p => new { p.Id, p.VarName, p.VersionSort, p.Creator, p.PackageName, p.CanonicalVarFileId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var familyMap = new Dictionary<string, List<AvailableVersion>>(StringComparer.Ordinal);
        var idToName = new Dictionary<long, string>();
        var idToCanonical = new Dictionary<long, long>();
        foreach (var p in allVersions)
        {
            idToName[p.Id] = p.VarName;
            if (p.CanonicalVarFileId is long vf)
                idToCanonical[p.Id] = vf;
            var familyKey = IdentityFold.Compute($"{p.Creator}.{p.PackageName}");
            if (!familyMap.TryGetValue(familyKey, out var list))
            {
                list = [];
                familyMap[familyKey] = list;
            }
            list.Add(new AvailableVersion(p.VersionSort, p.Id));
        }

        var aliasMap = await db.VarAliases.AsNoTracking()
            .Where(a => a.Scope == AliasScope.Global && a.ResolvedPackageId != null)
            .Select(a => new { a.MissingRefKey, PackageId = a.ResolvedPackageId!.Value })
            .ToDictionaryAsync(a => a.MissingRefKey, a => a.PackageId, cancellationToken)
            .ConfigureAwait(false);

        // BFS: walk every package reachable through resolvable deps (and alias targets).
        var visitedPackages = new HashSet<long>(activePackageIds);
        var frontier = new List<long>(activePackageIds);
        var byRef = new Dictionary<string, RefAccum>(StringComparer.OrdinalIgnoreCase);

        while (frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = new List<long>();

            for (var offset = 0; offset < frontier.Count; offset += FrontierChunkSize)
            {
                var chunk = frontier.Skip(offset).Take(FrontierChunkSize).ToList();
                var varFileIds = chunk
                    .Where(id => idToCanonical.ContainsKey(id))
                    .Select(id => idToCanonical[id])
                    .Distinct()
                    .ToList();
                if (varFileIds.Count == 0)
                    continue;

                var edges = await (
                        from d in db.Dependencies.AsNoTracking()
                        join v in db.VarFiles.AsNoTracking() on d.VarFileId equals v.Id
                        where varFileIds.Contains(d.VarFileId) && d.RefKind != RefKind.Self && v.PackageId != null
                        select new { SourcePackageId = v.PackageId!.Value, d.DependsOnRefRaw })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var edge in edges)
                {
                    if (!byRef.TryGetValue(edge.DependsOnRefRaw, out var accum))
                    {
                        accum = ResolveRef(edge.DependsOnRefRaw, familyMap, idToName, aliasMap);
                        byRef[edge.DependsOnRefRaw] = accum;
                    }
                    accum.NeededBy.Add(edge.SourcePackageId);

                    // Expand into packages we can actually walk (real/closest/alias targets).
                    if (accum.ExpandPackageId is { } expandId && visitedPackages.Add(expandId))
                        next.Add(expandId);
                }
            }

            frontier = next;
        }

        var entries = byRef
            .Select(kv => kv.Value.ToEntry(kv.Key))
            .OrderByDescending(e => e.NeededByCount)
            .ThenBy(e => e.Ref, StringComparer.Ordinal)
            .ToList();

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

        // Exact/Latest/Closest substitutes that aren't already linked — skip Alias (MissingVarLink on rebuild).
        // Deep analyze already listed transitive found packages, so activating them pulls the full tree.
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

        if (analysis.Entries.Any(e => e.Via == InstalledDepsResolveVia.Alias))
            return await _activator.RebuildActiveAsync(cancellationToken).ConfigureAwait(false);

        return new MissingLogActivation(0, 0, 0, 0);
    }

    public Task<MissingLogActivation> ActivateFoundAsync(
        IReadOnlyList<string> varNames,
        CancellationToken cancellationToken = default) =>
        _activator.ActivateAsync(varNames, cancellationToken);

    private static RefAccum ResolveRef(
        string raw,
        Dictionary<string, List<AvailableVersion>> familyMap,
        Dictionary<long, string> idToName,
        Dictionary<string, long> aliasMap)
    {
        var parsed = DependencyRef.Parse(raw);
        if (parsed.IsFailure)
            return RefAccum.Missing();

        var available = familyMap.GetValueOrDefault(parsed.Value.FamilyKey) ?? [];
        var resolution = VersionResolver.Resolve(parsed.Value, available);
        if (resolution is not null)
        {
            var via = MapVia(resolution.Via);
            if (!idToName.TryGetValue(resolution.PackageId, out var varName) || string.IsNullOrWhiteSpace(varName))
                return RefAccum.Missing();

            return new RefAccum(
                InLibrary: true,
                ResolvedVarName: varName,
                Via: via,
                NeedsAlias: via == InstalledDepsResolveVia.Closest,
                ExpandPackageId: resolution.PackageId);
        }

        var refKey = IdentityFold.Compute(raw);
        if (aliasMap.TryGetValue(refKey, out var aliasTargetId)
            && idToName.TryGetValue(aliasTargetId, out var aliasName)
            && !string.IsNullOrWhiteSpace(aliasName))
        {
            return new RefAccum(
                InLibrary: true,
                ResolvedVarName: aliasName,
                Via: InstalledDepsResolveVia.Alias,
                NeedsAlias: false,
                ExpandPackageId: aliasTargetId);
        }

        return RefAccum.Missing();
    }

    private sealed class RefAccum(
        bool InLibrary,
        string? ResolvedVarName,
        InstalledDepsResolveVia Via,
        bool NeedsAlias,
        long? ExpandPackageId)
    {
        public HashSet<long> NeededBy { get; } = [];
        public bool InLibrary { get; } = InLibrary;
        public string? ResolvedVarName { get; } = ResolvedVarName;
        public InstalledDepsResolveVia Via { get; } = Via;
        public bool NeedsAlias { get; } = NeedsAlias;
        public long? ExpandPackageId { get; } = ExpandPackageId;

        public static RefAccum Missing() =>
            new(false, null, InstalledDepsResolveVia.None, NeedsAlias: true, ExpandPackageId: null);

        public InstalledDepsEntry ToEntry(string @ref) =>
            new(@ref, InLibrary, ResolvedVarName, Via, NeededBy.Count, NeedsAlias);
    }

    private static InstalledDepsResolveVia MapVia(ResolvedVia via) => via switch
    {
        ResolvedVia.Exact => InstalledDepsResolveVia.Exact,
        ResolvedVia.Latest => InstalledDepsResolveVia.Latest,
        ResolvedVia.Closest => InstalledDepsResolveVia.Closest,
        ResolvedVia.Alias => InstalledDepsResolveVia.Alias,
        _ => InstalledDepsResolveVia.None,
    };
}
