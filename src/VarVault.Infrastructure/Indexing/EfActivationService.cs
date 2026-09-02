using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VarVault.Common;
using VarVault.Common.Threading;
using VarVault.Domain.Activation;
using VarVault.Domain.Analyzer;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;
using VarVault.Sdk.Settings;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// EF activation link builder. Resolves a preset's members + forward-dependency closure to the hottest
/// online copy of each package and <b>materializes real per-var file symlinks on disk</b> under the active
/// profile's <c>___VarsLink___</c> (install) and <c>___MissingVarLink___</c> (alias) folders, keeping the
/// <c>ActivationLink</c> rows a faithful mirror of the filesystem. The link filename IS the VaM identity
/// (<c>Creator.Package.Version.var</c>). Idempotent: re-running reconciles disk to the desired set.
/// (Spec 21; checklist 22 · P3/P4/P5.)
/// </summary>
public sealed class EfActivationService(
    VarVaultDbContext db,
    IClock clock,
    IDependencyGraph graph,
    ISettingsService settings,
    IVamProfileService profiles,
    ISymlinkService symlinks,
    IWriteQueue writeQueue,
    IProfilePackageLinkService profileLinks,
    IPresetService presets,
    ActivationUsageFeedCoordinator usageFeed,
    ILogger<EfActivationService> logger) : IActivationService
{
    // Serializes filesystem link operations so two concurrent activations can't race on a profile dir. (T7.3)
    private static readonly AsyncLock FsLock = new();
    private const int CopyPickChunkSize = 500;

    public async Task<ActivationBuildResult> BuildProfileLinksAsync(long presetId, CancellationToken cancellationToken = default)
    {
        var (members, aliases, unresolvedMembers) = await ResolvedMembersAsync(presetId, cancellationToken).ConfigureAwait(false);
        return await RecomputeAsync(presetId, members, aliases, unresolvedMembers, recordUsage: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ActivationBuildResult> DeactivateAsync(long presetId, long packageId, CancellationToken cancellationToken = default)
    {
        var (members, aliases, unresolvedMembers) = await ResolvedMembersAsync(presetId, cancellationToken).ConfigureAwait(false);
        members.Remove(packageId); // ref-counting: deps stay if another active member still needs them
        // Deactivate rebuilds links — must NOT count as usage (G1).
        return await RecomputeAsync(presetId, members, aliases, unresolvedMembers, recordUsage: false, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> RescueAsync(long profileId, CancellationToken cancellationToken = default)
    {
        var owned = await db.ActivationLinks.AsNoTracking()
            .Where(l => l.ProfileId == profileId && l.RequestedByPresetId != null)
            .Select(l => new { l.Id, l.LinkPath })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        using (await FsLock.AcquireAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var link in owned)
                DeleteLinkFile(link.LinkPath);
            if (owned.Count > 0)
            {
                var ids = owned.Select(l => l.Id).ToList();
                await writeQueue.EnqueueScopedAsync(async (sp, ct) =>
                {
                    var scopedDb = sp.GetRequiredService<VarVaultDbContext>();
                    var rows = await scopedDb.ActivationLinks.Where(l => ids.Contains(l.Id)).ToListAsync(ct).ConfigureAwait(false);
                    scopedDb.ActivationLinks.RemoveRange(rows);
                    await scopedDb.SaveChangesAsync(ct).ConfigureAwait(false);
                }, WritePriority.Interactive, cancellationToken).ConfigureAwait(false);
            }
        }
        await profileLinks.SyncFromActivationLinksAsync(profileId, cancellationToken).ConfigureAwait(false);
        await profileLinks.RefreshActiveProfileReadModelAsync(cancellationToken).ConfigureAwait(false);
        return owned.Count;
    }

    public async Task<Result<int>> RescueActiveAsync(CancellationToken cancellationToken = default)
    {
        var root = await VamRootAsync(cancellationToken).ConfigureAwait(false);
        if (root.IsFailure)
        {
            logger.LogWarning("Rescue skipped: {Reason}", root.Error.Message);
            return Result.Failure<int>("rescue.novamroot", "VaM install path is not set — rescue skipped.");
        }
        var vamRoot = root.Value;
        if (!Directory.Exists(vamRoot))
        {
            logger.LogWarning("Rescue skipped: VaM path does not exist: {VamRoot}", vamRoot);
            return Result.Failure<int>("rescue.badpath", "VaM install path does not exist — rescue skipped.");
        }
        var activeName = profiles.ActiveProfile(vamRoot);
        if (string.IsNullOrWhiteSpace(activeName))
        {
            logger.LogWarning("Rescue skipped: no active AddonPackages profile under {VamRoot}", vamRoot);
            return Result.Failure<int>("rescue.noactive", "No active AddonPackages profile — rescue skipped.");
        }
        var profile = await db.Profiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == activeName, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null)
        {
            var all = await db.Profiles.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
            profile = all.FirstOrDefault(p => string.Equals(p.Name, activeName, StringComparison.OrdinalIgnoreCase));
        }
        if (profile is null)
        {
            logger.LogWarning("Rescue skipped: catalog has no Profile row for active '{ActiveName}'", activeName);
            return Result.Failure<int>("rescue.noprofile",
                $"Active profile '{activeName}' is not in the catalog — rescue skipped.");
        }
        var count = await RescueAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success(count);
    }

    public async Task<int> CleanTempLinksAsync(long profileId, CancellationToken cancellationToken = default)
    {
        var temps = await db.ActivationLinks.AsNoTracking()
            .Where(l => l.ProfileId == profileId && l.LinkKind == LinkKind.Temp)
            .Select(l => new { l.Id, l.LinkPath })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        using (await FsLock.AcquireAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var link in temps)
                DeleteLinkFile(link.LinkPath);
            if (temps.Count > 0)
            {
                var ids = temps.Select(l => l.Id).ToList();
                await writeQueue.EnqueueScopedAsync(async (sp, ct) =>
                {
                    var scopedDb = sp.GetRequiredService<VarVaultDbContext>();
                    var rows = await scopedDb.ActivationLinks.Where(l => ids.Contains(l.Id)).ToListAsync(ct).ConfigureAwait(false);
                    scopedDb.ActivationLinks.RemoveRange(rows);
                    await scopedDb.SaveChangesAsync(ct).ConfigureAwait(false);
                }, WritePriority.Interactive, cancellationToken).ConfigureAwait(false);
            }
        }
        return temps.Count;
    }

    public async Task<int> ReconcileProfilesAsync(CancellationToken cancellationToken = default)
    {
        var root = await VamRootAsync(cancellationToken).ConfigureAwait(false);
        if (root.IsFailure)
            return 0;
        var vamRoot = root.Value;
        var activeName = profiles.ActiveProfile(vamRoot);

        var all = await db.Profiles.AsNoTracking()
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var pruned = 0;
        var updates = new List<(long Id, bool IsActive)>();
        var removedProfileIds = new List<long>();
        foreach (var p in all)
        {
            var dirExists = Directory.Exists(ActivationPaths.ProfileDir(vamRoot, p.Name));
            if (!dirExists)
            {
                removedProfileIds.Add(p.Id);
                pruned++;
                continue;
            }
            updates.Add((p.Id, string.Equals(p.Name, activeName, StringComparison.OrdinalIgnoreCase)));
        }

        if (removedProfileIds.Count > 0 || updates.Count > 0)
        {
            await writeQueue.EnqueueScopedAsync(async (sp, ct) =>
            {
                var scopedDb = sp.GetRequiredService<VarVaultDbContext>();
                foreach (var profileId in removedProfileIds)
                {
                    var links = await scopedDb.ActivationLinks.Where(l => l.ProfileId == profileId).ToListAsync(ct).ConfigureAwait(false);
                    scopedDb.ActivationLinks.RemoveRange(links);
                    var profile = await scopedDb.Profiles.FirstAsync(p => p.Id == profileId, ct).ConfigureAwait(false);
                    scopedDb.Profiles.Remove(profile);
                }
                foreach (var (id, isActive) in updates)
                {
                    var profile = await scopedDb.Profiles.FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
                    if (profile is not null)
                        profile.IsActive = isActive;
                }
                await scopedDb.SaveChangesAsync(ct).ConfigureAwait(false);
            }, WritePriority.Interactive, cancellationToken).ConfigureAwait(false);
        }
        return pruned;
    }

    // ── resolution ────────────────────────────────────────────────────────────

    private async Task<Result<string>> VamRootAsync(CancellationToken cancellationToken)
    {
        var root = await settings.GetAsync(SettingKeys.VamPath, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(root)
            ? Result.Failure<string>("activation.novamroot", "VaM install path is not set.")
            : Result.Success(root);
    }

    private sealed record AliasMapping(string MissingRefKey, string MissingRefRaw, long TargetPackageId);
    private sealed record ProfileInfo(long Id, string Name);
    private sealed record LinkPersistRow(string LinkPath, long VarFileId, LinkKind Kind, ActivationReason Reason, string? AliasKey);

    private async Task<(HashSet<long> Members, List<AliasMapping> Aliases, IReadOnlyList<string> UnresolvedMembers)> ResolvedMembersAsync(
        long presetId, CancellationToken cancellationToken)
    {
        await presets.RefreshMemberResolutionsAsync(presetId, cancellationToken).ConfigureAwait(false);

        var members = await db.PresetMembers.AsNoTracking()
            .Where(m => m.PresetId == presetId)
            .Select(m => new { m.ResolvedPackageId, m.PackageRefKey, m.PackageRefRaw })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var ids = new HashSet<long>();
        var unresolved = new List<(string Key, string Raw)>();
        foreach (var m in members)
        {
            if (m.ResolvedPackageId is { } id)
                ids.Add(id);
            else
                unresolved.Add((m.PackageRefKey, m.PackageRefRaw));
        }

        var aliasRows = await db.VarAliases.AsNoTracking()
            .Where(a => a.ResolvedPackageId != null
                        && (a.Scope == AliasScope.Global || a.PresetId == presetId))
            .Select(a => new { a.MissingRefKey, a.MissingRefRaw, TargetId = a.ResolvedPackageId!.Value })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var aliases = aliasRows
            .GroupBy(a => a.MissingRefKey, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(a => new AliasMapping(a.MissingRefKey, a.MissingRefRaw, a.TargetId))
            .ToList();
        var aliasedKeys = aliases.Select(a => a.MissingRefKey).ToHashSet(StringComparer.Ordinal);

        var stillUnresolved = new List<string>();
        foreach (var u in unresolved)
        {
            if (!aliasedKeys.Contains(u.Key))
                stillUnresolved.Add(u.Raw);
        }

        return (ids, aliases, stillUnresolved);
    }

    // ── the core recompute (materializes disk + mirrors rows) ──────────────────

    private async Task<ActivationBuildResult> RecomputeAsync(
        long presetId, HashSet<long> memberSet, IReadOnlyList<AliasMapping> aliases,
        IReadOnlyList<string> unresolvedMembers, bool recordUsage, CancellationToken cancellationToken)
    {
        var preset = await db.LoadingPresets.AsNoTracking()
            .Where(p => p.Id == presetId)
            .Select(p => new { p.Id, p.Name })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (preset is null)
            return new ActivationBuildResult(0, 0, 0);

        var rootResult = await VamRootAsync(cancellationToken).ConfigureAwait(false);
        if (rootResult.IsFailure)
        {
            logger.LogWarning("Activation skipped for preset {PresetId}: {Reason}", presetId, rootResult.Error.Message);
            return new ActivationBuildResult(0, 0, 0, PathUnavailable: 1,
                UnresolvedDependencies: unresolvedMembers.Count);
        }
        var vamRoot = rootResult.Value;
        if (!Directory.Exists(vamRoot))
        {
            logger.LogWarning("Activation skipped for preset {PresetId}: VaM path does not exist: {VamRoot}", presetId, vamRoot);
            return new ActivationBuildResult(0, 0, 0, PathUnavailable: 1,
                UnresolvedDependencies: unresolvedMembers.Count);
        }
        var now = clock.UtcNow.UtcDateTime;
        var profile = await EnsureProfileAsync(presetId, preset.Name, vamRoot, now, cancellationToken).ConfigureAwait(false);

        var varsLinkDir = ActivationPaths.VarsLinkDir(vamRoot, profile.Name);
        var missingLinkDir = ActivationPaths.MissingVarLinkDir(vamRoot, profile.Name);

        var full = new HashSet<long>(memberSet);
        var unresolvedRefs = new HashSet<string>(unresolvedMembers, StringComparer.OrdinalIgnoreCase);
        foreach (var id in memberSet)
        {
            var closure = await graph.ForwardClosureDetailedAsync(id, cancellationToken).ConfigureAwait(false);
            foreach (var dep in closure.PackageIds)
                full.Add(dep);
            foreach (var edge in closure.UnresolvedEdges)
                unresolvedRefs.Add(edge.DependsOnRefRaw);
        }
        foreach (var alias in aliases)
        {
            var closure = await graph.ForwardClosureDetailedAsync(alias.TargetPackageId, cancellationToken).ConfigureAwait(false);
            foreach (var dep in closure.PackageIds)
                full.Add(dep);
            foreach (var edge in closure.UnresolvedEdges)
                unresolvedRefs.Add(edge.DependsOnRefRaw);
        }

        var desired = new Dictionary<string, DesiredLink>(StringComparer.OrdinalIgnoreCase);
        var offlinePackages = new HashSet<long>();
        var pickIds = new HashSet<long>(full);
        foreach (var alias in aliases)
            pickIds.Add(alias.TargetPackageId);
        var hottestByPackage = await PickHottestOnlineCopiesAsync(pickIds.ToList(), cancellationToken).ConfigureAwait(false);
        foreach (var packageId in full)
        {
            if (!hottestByPackage.TryGetValue(packageId, out var copy))
            {
                offlinePackages.Add(packageId);
                continue;
            }

            var fileName = ActivationPaths.LinkFileName(copy.VarName);
            if (fileName.IsFailure)
            {
                logger.LogWarning("Skipping install link for {VarName}: {Reason}", copy.VarName, fileName.Error.Message);
                offlinePackages.Add(packageId);
                continue;
            }
            var linkPath = Path.Combine(varsLinkDir, fileName.Value);
            var reason = memberSet.Contains(packageId) ? ActivationReason.Explicit : ActivationReason.DependencyOf;
            desired[linkPath] = new DesiredLink(linkPath, ActivationPaths.SourcePath(copy.MountPath, copy.RelativePath),
                copy.VarFileId, packageId, LinkKind.Install, reason, AliasKey: null);
        }

        foreach (var alias in aliases)
        {
            if (!hottestByPackage.TryGetValue(alias.TargetPackageId, out var copy))
            {
                offlinePackages.Add(alias.TargetPackageId);
                continue;
            }

            var fileName = ActivationPaths.AliasLinkFileName(alias.MissingRefRaw, copy.VarName);
            if (fileName.IsFailure)
            {
                logger.LogWarning("Skipping alias link for {MissingRef}: {Reason}", alias.MissingRefRaw, fileName.Error.Message);
                continue;
            }
            var linkPath = Path.Combine(missingLinkDir, fileName.Value);
            desired[linkPath] = new DesiredLink(linkPath, ActivationPaths.SourcePath(copy.MountPath, copy.RelativePath),
                copy.VarFileId, alias.TargetPackageId, LinkKind.Alias, ActivationReason.Explicit, AliasKey: alias.MissingRefKey);
        }

        var missing = offlinePackages.Count;

        var existingPaths = await db.ActivationLinks.AsNoTracking()
            .Where(l => l.ProfileId == profile.Id && l.RequestedByPresetId == presetId
                        && (l.LinkKind == LinkKind.Install || l.LinkKind == LinkKind.Alias))
            .Select(l => l.LinkPath)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingPathSet = existingPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newlyCreated = new List<string>();
        var present = 0;
        var installedPackageIds = new HashSet<long>();
        var upserts = new List<LinkPersistRow>();
        var removePaths = new List<string>();
        var removed = 0;

        using (await FsLock.AcquireAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var d in desired.Values)
            {
                var outcome = EnsureFileLink(d.LinkPath, d.SourcePath);
                switch (outcome)
                {
                    case LinkOutcome.Privilege:
                        foreach (var path in newlyCreated) DeleteLinkFile(path);
                        logger.LogWarning("Activation aborted for preset {PresetId}: symlink privilege (Developer Mode).", presetId);
                        return new ActivationBuildResult(0, 0, 0, PrivilegeFailures: 1,
                            UnresolvedDependencies: unresolvedRefs.Count);
                    case LinkOutcome.Created:
                    case LinkOutcome.Recreated:
                        newlyCreated.Add(d.LinkPath);
                        break;
                    case LinkOutcome.RefusedCollision:
                        logger.LogWarning("Refusing to overwrite non-link at {LinkPath}; skipping.", d.LinkPath);
                        continue;
                    case LinkOutcome.Failed:
                        logger.LogWarning("Failed to create link {LinkPath}; skipping.", d.LinkPath);
                        continue;
                }
                present++;
                if (d.Kind == LinkKind.Install)
                    installedPackageIds.Add(d.PackageId);
                upserts.Add(new LinkPersistRow(d.LinkPath, d.VarFileId, d.Kind, d.Reason, d.AliasKey));
            }

            foreach (var path in existingPathSet)
            {
                if (desired.ContainsKey(path))
                    continue;
                DeleteLinkFile(path);
                removePaths.Add(path);
                removed++;
            }
        }

        var profileIsActive = string.Equals(profile.Name, profiles.ActiveProfile(vamRoot), StringComparison.OrdinalIgnoreCase);
        await PersistActivationLinksAsync(profile.Id, presetId, profileIsActive, upserts, removePaths, cancellationToken)
            .ConfigureAwait(false);

        await profileLinks.SyncFromActivationLinksAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (profileIsActive)
            await profileLinks.RefreshActiveProfileReadModelAsync(cancellationToken).ConfigureAwait(false);

        if (recordUsage && installedPackageIds.Count > 0)
            RecordActivateUsageBestEffort(installedPackageIds.ToList());

        return new ActivationBuildResult(present, removed, missing, UnresolvedDependencies: unresolvedRefs.Count);
    }

    private async Task PersistActivationLinksAsync(
        long profileId,
        long presetId,
        bool profileIsActive,
        IReadOnlyList<LinkPersistRow> upserts,
        IReadOnlyList<string> removePaths,
        CancellationToken cancellationToken)
    {
        await writeQueue.EnqueueScopedAsync(async (sp, ct) =>
        {
            var scopedDb = sp.GetRequiredService<VarVaultDbContext>();
            await using var tx = await scopedDb.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var profile = await scopedDb.Profiles.FirstAsync(p => p.Id == profileId, ct).ConfigureAwait(false);
            profile.IsActive = profileIsActive;

            var existingByPath = await scopedDb.ActivationLinks
                .Where(l => l.ProfileId == profileId && l.RequestedByPresetId == presetId
                            && (l.LinkKind == LinkKind.Install || l.LinkKind == LinkKind.Alias))
                .ToDictionaryAsync(l => l.LinkPath, StringComparer.OrdinalIgnoreCase, ct)
                .ConfigureAwait(false);

            foreach (var row in upserts)
            {
                if (existingByPath.TryGetValue(row.LinkPath, out var existing))
                {
                    existing.VarFileId = row.VarFileId;
                    existing.LinkKind = row.Kind;
                    existing.Reason = row.Reason;
                    existing.AliasedMissingRefKey = row.AliasKey;
                }
                else
                {
                    scopedDb.ActivationLinks.Add(new ActivationLink
                    {
                        ProfileId = profileId,
                        VarFileId = row.VarFileId,
                        LinkPath = row.LinkPath,
                        LinkKind = row.Kind,
                        LinkType = LinkType.Symlink,
                        Reason = row.Reason,
                        AliasedMissingRefKey = row.AliasKey,
                        RequestedByPresetId = presetId,
                    });
                }
            }

            foreach (var path in removePaths)
            {
                if (existingByPath.TryGetValue(path, out var orphan))
                    scopedDb.ActivationLinks.Remove(orphan);
            }

            await scopedDb.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }, WritePriority.Interactive, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Best-effort: never fail activation because usage scoring failed. (G1.)</summary>
    private void RecordActivateUsageBestEffort(IReadOnlyList<long> packageIds) =>
        usageFeed.Enqueue(packageIds);

    private enum LinkOutcome { Created, Unchanged, Recreated, RefusedCollision, Privilege, Failed }

    private LinkOutcome EnsureFileLink(string linkPath, string targetPath)
    {
        if (symlinks.IsLink(linkPath))
        {
            var current = symlinks.ResolveTarget(linkPath);
            if (string.Equals(current, targetPath, StringComparison.OrdinalIgnoreCase))
                return LinkOutcome.Unchanged;
            var del = symlinks.DeleteLink(linkPath);
            if (del.IsFailure)
                return del.Error.Code == "symlink.privilege" ? LinkOutcome.Privilege : LinkOutcome.Failed;
            var recreate = symlinks.CreateFile(linkPath, targetPath);
            return Classify(recreate, LinkOutcome.Recreated);
        }

        if (File.Exists(linkPath) || Directory.Exists(linkPath))
            return LinkOutcome.RefusedCollision;

        var create = symlinks.CreateFile(linkPath, targetPath);
        return Classify(create, LinkOutcome.Created);

        static LinkOutcome Classify(Result r, LinkOutcome success) =>
            r.IsSuccess ? success
            : r.Error.Code == "symlink.privilege" ? LinkOutcome.Privilege
            : LinkOutcome.Failed;
    }

    private void DeleteLinkFile(string linkPath)
    {
        var result = symlinks.DeleteLink(linkPath);
        if (result.IsFailure)
            logger.LogWarning("Could not delete link {LinkPath}: {Reason}", linkPath, result.Error.Message);
    }

    private sealed record DesiredLink(
        string LinkPath, string SourcePath, long VarFileId, long PackageId, LinkKind Kind, ActivationReason Reason, string? AliasKey);

    private sealed record HotCopy(long VarFileId, string VarName, string MountPath, string RelativePath, int Tier, int PriorityInTier);

    private async Task<Dictionary<long, HotCopy>> PickHottestOnlineCopiesAsync(
        IReadOnlyList<long> packageIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, HotCopy>();
        if (packageIds.Count == 0)
            return result;

        var distinct = packageIds.Where(id => id > 0).Distinct().ToList();
        for (var offset = 0; offset < distinct.Count; offset += CopyPickChunkSize)
        {
            var chunk = distinct.Skip(offset).Take(CopyPickChunkSize).ToList();
            var rows = await (
                    from v in db.VarFiles.AsNoTracking()
                    join r in db.Repositories.AsNoTracking() on v.RepositoryId equals r.Id
                    join p in db.Packages.AsNoTracking() on v.PackageId equals p.Id
                    where v.PackageId != null && chunk.Contains(v.PackageId.Value) && r.IsOnline && r.IsEnabled
                    select new
                    {
                        PackageId = v.PackageId!.Value,
                        v.Id,
                        p.VarName,
                        r.MountPath,
                        v.RelativePath,
                        r.Tier,
                        r.PriorityInTier,
                    })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var row in rows)
            {
                var candidate = new HotCopy(row.Id, row.VarName, row.MountPath, row.RelativePath, row.Tier, row.PriorityInTier);
                if (!result.TryGetValue(row.PackageId, out var existing)
                    || candidate.Tier < existing.Tier
                    || (candidate.Tier == existing.Tier && candidate.PriorityInTier < existing.PriorityInTier)
                    || (candidate.Tier == existing.Tier && candidate.PriorityInTier == existing.PriorityInTier && candidate.VarFileId < existing.VarFileId))
                {
                    result[row.PackageId] = candidate;
                }
            }
        }

        return result;
    }

    private async Task<ProfileInfo> EnsureProfileAsync(
        long presetId, string presetName, string vamRoot, DateTime now, CancellationToken cancellationToken)
    {
        var presetProfileId = await db.LoadingPresets.AsNoTracking()
            .Where(p => p.Id == presetId)
            .Select(p => p.ProfileId)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (presetProfileId is long pid)
        {
            var profile = await db.Profiles.AsNoTracking()
                .Where(p => p.Id == pid)
                .Select(p => new ProfileInfo(p.Id, p.Name))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (profile is not null)
            {
                EnsureProfileDirs(vamRoot, profile.Name);
                return profile;
            }
        }

        var created = await writeQueue.EnqueueScopedAsync(async (sp, ct) =>
        {
            var scopedDb = sp.GetRequiredService<VarVaultDbContext>();
            var loadingPreset = await scopedDb.LoadingPresets.FirstAsync(p => p.Id == presetId, ct).ConfigureAwait(false);
            var profile = new Profile
            {
                Name = presetName,
                DirPath = $"{ActivationPaths.SwitchDirName}/{presetName}",
                CreatedAt = now,
                UpdatedAt = now,
            };
            scopedDb.Profiles.Add(profile);
            await scopedDb.SaveChangesAsync(ct).ConfigureAwait(false);
            loadingPreset.ProfileId = profile.Id;
            loadingPreset.UpdatedAt = now;
            await scopedDb.SaveChangesAsync(ct).ConfigureAwait(false);
            return new ProfileInfo(profile.Id, profile.Name);
        }, WritePriority.Interactive, cancellationToken).ConfigureAwait(false);

        EnsureProfileDirs(vamRoot, created.Name);
        return created;
    }

    private static void EnsureProfileDirs(string vamRoot, string profileName)
    {
        Directory.CreateDirectory(ActivationPaths.ProfileDir(vamRoot, profileName));
        Directory.CreateDirectory(ActivationPaths.VarsLinkDir(vamRoot, profileName));
        Directory.CreateDirectory(ActivationPaths.MissingVarLinkDir(vamRoot, profileName));
    }
}
