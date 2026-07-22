using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VarVault.Common;
using VarVault.Common.Threading;
using VarVault.Domain.Activation;
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
    ILogger<EfActivationService> logger) : IActivationService
{
    // Serializes filesystem link operations so two concurrent activations can't race on a profile dir. (T7.3)
    private static readonly AsyncLock FsLock = new();

    public async Task<ActivationBuildResult> BuildProfileLinksAsync(long presetId, CancellationToken cancellationToken = default)
    {
        var (members, aliases, unresolvedMembers) = await ResolvedMembersAsync(presetId, cancellationToken).ConfigureAwait(false);
        return await RecomputeAsync(presetId, members, aliases, unresolvedMembers, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActivationBuildResult> DeactivateAsync(long presetId, long packageId, CancellationToken cancellationToken = default)
    {
        var (members, aliases, unresolvedMembers) = await ResolvedMembersAsync(presetId, cancellationToken).ConfigureAwait(false);
        members.Remove(packageId); // ref-counting: deps stay if another active member still needs them
        return await RecomputeAsync(presetId, members, aliases, unresolvedMembers, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RescueAsync(long profileId, CancellationToken cancellationToken = default)
    {
        // Remove every app-created link (a preset owns it); user-made links (null attribution) survive.
        var owned = await db.ActivationLinks
            .Where(l => l.ProfileId == profileId && l.RequestedByPresetId != null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        using (await FsLock.AcquireAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var link in owned)
                DeleteLinkFile(link.LinkPath);
            db.ActivationLinks.RemoveRange(owned);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        await profileLinks.SyncFromActivationLinksAsync(profileId, cancellationToken).ConfigureAwait(false);
        await profileLinks.RefreshActiveProfileReadModelAsync(cancellationToken).ConfigureAwait(false);
        return owned.Count;
    }

    public async Task<int> CleanTempLinksAsync(long profileId, CancellationToken cancellationToken = default)
    {
        var temps = await db.ActivationLinks
            .Where(l => l.ProfileId == profileId && l.LinkKind == LinkKind.Temp)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        using (await FsLock.AcquireAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var link in temps)
                DeleteLinkFile(link.LinkPath);
            db.ActivationLinks.RemoveRange(temps);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
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

        var all = await db.Profiles.ToListAsync(cancellationToken).ConfigureAwait(false);
        var pruned = 0;
        foreach (var p in all)
        {
            var dirExists = Directory.Exists(ActivationPaths.ProfileDir(vamRoot, p.Name));
            if (!dirExists)
            {
                var links = await db.ActivationLinks.Where(l => l.ProfileId == p.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
                db.ActivationLinks.RemoveRange(links);
                db.Profiles.Remove(p);
                pruned++;
                continue;
            }
            p.IsActive = string.Equals(p.Name, activeName, StringComparison.OrdinalIgnoreCase);
        }
        await SaveAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task<(HashSet<long> Members, List<AliasMapping> Aliases, IReadOnlyList<string> UnresolvedMembers)> ResolvedMembersAsync(
        long presetId, CancellationToken cancellationToken)
    {
        // Fresh .latest / formerly-missing snapshots before materializing links.
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

        var aliases = new List<AliasMapping>();
        var stillUnresolved = new List<string>();
        if (unresolved.Count > 0)
        {
            var unresolvedKeys = unresolved.Select(u => u.Key).ToList();
            // Persistent aliases (global + per-preset) re-apply automatically every build — no re-setup. (3.9)
            var matched = await db.VarAliases.AsNoTracking()
                .Where(a => a.ResolvedPackageId != null
                            && unresolvedKeys.Contains(a.MissingRefKey)
                            && (a.Scope == AliasScope.Global || a.PresetId == presetId))
                .Select(a => new { a.MissingRefKey, a.MissingRefRaw, TargetId = a.ResolvedPackageId!.Value })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var aliasedKeys = matched.Select(a => a.MissingRefKey).ToHashSet(StringComparer.Ordinal);
            foreach (var a in matched)
                aliases.Add(new AliasMapping(a.MissingRefKey, a.MissingRefRaw, a.TargetId));
            foreach (var u in unresolved)
            {
                if (!aliasedKeys.Contains(u.Key))
                    stillUnresolved.Add(u.Raw);
            }
        }

        return (ids, aliases, stillUnresolved);
    }

    // ── the core recompute (materializes disk + mirrors rows) ──────────────────

    private async Task<ActivationBuildResult> RecomputeAsync(
        long presetId, HashSet<long> memberSet, IReadOnlyList<AliasMapping> aliases,
        IReadOnlyList<string> unresolvedMembers, CancellationToken cancellationToken)
    {
        var preset = await db.LoadingPresets.FirstOrDefaultAsync(p => p.Id == presetId, cancellationToken).ConfigureAwait(false);
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
            // Defensive: never create a bogus profile tree under a mistyped/offline path. (T6.3a)
            logger.LogWarning("Activation skipped for preset {PresetId}: VaM path does not exist: {VamRoot}", presetId, vamRoot);
            return new ActivationBuildResult(0, 0, 0, PathUnavailable: 1,
                UnresolvedDependencies: unresolvedMembers.Count);
        }
        var now = clock.UtcNow.UtcDateTime;
        var profile = await EnsureProfileAsync(preset, vamRoot, now, cancellationToken).ConfigureAwait(false);

        var varsLinkDir = ActivationPaths.VarsLinkDir(vamRoot, profile.Name);
        var missingLinkDir = ActivationPaths.MissingVarLinkDir(vamRoot, profile.Name);

        // Forward closure: members + their deps, plus alias-target deps (the alias link itself stands in
        // for the target under the missing name, but the target's dependencies still need installing).
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
        // Alias targets are NOT added to `full` — the alias link stands in for that package. Their
        // dependency closure still is, and offline targets are counted once via offlinePackages below.
        foreach (var alias in aliases)
        {
            var closure = await graph.ForwardClosureDetailedAsync(alias.TargetPackageId, cancellationToken).ConfigureAwait(false);
            foreach (var dep in closure.PackageIds)
                full.Add(dep);
            foreach (var edge in closure.UnresolvedEdges)
                unresolvedRefs.Add(edge.DependsOnRefRaw);
        }

        // Desired install links, keyed by absolute link path.
        var desired = new Dictionary<string, DesiredLink>(StringComparer.OrdinalIgnoreCase);
        var offlinePackages = new HashSet<long>();
        foreach (var packageId in full)
        {
            var copy = await PickHottestOnlineCopyAsync(packageId, cancellationToken).ConfigureAwait(false);
            if (copy is null) { offlinePackages.Add(packageId); continue; }

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
                copy.VarFileId, LinkKind.Install, reason, AliasKey: null);
        }

        // Desired alias links (named after the still-missing ref) → the owned target's file.
        // Count offline targets once here — they are not in `full`, so they won't double-count.
        foreach (var alias in aliases)
        {
            var copy = await PickHottestOnlineCopyAsync(alias.TargetPackageId, cancellationToken).ConfigureAwait(false);
            if (copy is null)
            {
                offlinePackages.Add(alias.TargetPackageId);
                continue;
            }

            var fileName = ActivationPaths.LinkFileName(alias.MissingRefRaw);
            if (fileName.IsFailure)
            {
                logger.LogWarning("Skipping alias link for {MissingRef}: {Reason}", alias.MissingRefRaw, fileName.Error.Message);
                continue;
            }
            var linkPath = Path.Combine(missingLinkDir, fileName.Value);
            desired[linkPath] = new DesiredLink(linkPath, ActivationPaths.SourcePath(copy.MountPath, copy.RelativePath),
                copy.VarFileId, LinkKind.Alias, ActivationReason.Explicit, AliasKey: alias.MissingRefKey);
        }

        var missing = offlinePackages.Count;

        // Existing app-owned links for this preset (install + alias); never touch user (null) or temp links.
        var existing = await db.ActivationLinks
            .Where(l => l.ProfileId == profile.Id && l.RequestedByPresetId == presetId
                        && (l.LinkKind == LinkKind.Install || l.LinkKind == LinkKind.Alias))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingByPath = new Dictionary<string, ActivationLink>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in existing)
            existingByPath[row.LinkPath] = row;

        using (await FsLock.AcquireAsync(cancellationToken).ConfigureAwait(false))
        {
            var newlyCreated = new List<string>();
            var present = 0;

            // Phase A — materialize desired links (creates first, so a privilege denial aborts atomically).
            foreach (var d in desired.Values)
            {
                var outcome = EnsureFileLink(d.LinkPath, d.SourcePath);
                switch (outcome)
                {
                    case LinkOutcome.Privilege:
                        foreach (var path in newlyCreated) DeleteLinkFile(path); // roll back this call
                        db.ChangeTracker.Clear();
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
                present++; // Created, Recreated, or Unchanged — the link is on disk

                // Upsert the row for this present link.
                if (existingByPath.TryGetValue(d.LinkPath, out var row))
                {
                    row.VarFileId = d.VarFileId;
                    row.LinkKind = d.Kind;
                    row.Reason = d.Reason;
                    row.AliasedMissingRefKey = d.AliasKey;
                }
                else
                {
                    db.ActivationLinks.Add(new ActivationLink
                    {
                        ProfileId = profile.Id,
                        VarFileId = d.VarFileId,
                        LinkPath = d.LinkPath,
                        LinkKind = d.Kind,
                        LinkType = LinkType.Symlink,
                        Reason = d.Reason,
                        AliasedMissingRefKey = d.AliasKey,
                        RequestedByPresetId = presetId,
                    });
                }
            }

            // Phase B — orphan sweep: owned links no longer desired → delete file + row.
            var removed = 0;
            foreach (var row in existing)
            {
                if (desired.ContainsKey(row.LinkPath))
                    continue;
                DeleteLinkFile(row.LinkPath);
                db.ActivationLinks.Remove(row);
                removed++;
            }

            profile.IsActive = string.Equals(profile.Name, profiles.ActiveProfile(vamRoot), StringComparison.OrdinalIgnoreCase);
            await SaveAsync(cancellationToken).ConfigureAwait(false);

            await profileLinks.SyncFromActivationLinksAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            if (profile.IsActive)
                await profileLinks.RefreshActiveProfileReadModelAsync(cancellationToken).ConfigureAwait(false);

            return new ActivationBuildResult(present, removed, missing, UnresolvedDependencies: unresolvedRefs.Count);
        }
    }

    private enum LinkOutcome { Created, Unchanged, Recreated, RefusedCollision, Privilege, Failed }

    private LinkOutcome EnsureFileLink(string linkPath, string targetPath)
    {
        if (symlinks.IsLink(linkPath))
        {
            var current = symlinks.ResolveTarget(linkPath);
            if (string.Equals(current, targetPath, StringComparison.OrdinalIgnoreCase))
                return LinkOutcome.Unchanged; // already correct
            var del = symlinks.DeleteLink(linkPath);
            if (del.IsFailure)
                return del.Error.Code == "symlink.privilege" ? LinkOutcome.Privilege : LinkOutcome.Failed;
            var recreate = symlinks.CreateFile(linkPath, targetPath);
            return Classify(recreate, LinkOutcome.Recreated);
        }

        if (File.Exists(linkPath) || Directory.Exists(linkPath))
            return LinkOutcome.RefusedCollision; // a real file/dir occupies the name — never clobber

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
        string LinkPath, string SourcePath, long VarFileId, LinkKind Kind, ActivationReason Reason, string? AliasKey);

    private sealed record HotCopy(long VarFileId, string VarName, string MountPath, string RelativePath);

    // The hottest online copy = the VarFile in the lowest-tier online repository. (3.4)
    private async Task<HotCopy?> PickHottestOnlineCopyAsync(long packageId, CancellationToken cancellationToken)
    {
        return await (
            from v in db.VarFiles
            join r in db.Repositories on v.RepositoryId equals r.Id
            join p in db.Packages on v.PackageId equals p.Id
            where v.PackageId == packageId && r.IsOnline && r.IsEnabled
            orderby r.Tier, r.PriorityInTier, v.Id
            select new HotCopy(v.Id, p.VarName, r.MountPath, v.RelativePath))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Profile> EnsureProfileAsync(LoadingPreset preset, string vamRoot, DateTime now, CancellationToken cancellationToken)
    {
        Profile? profile = null;
        if (preset.ProfileId is { } pid)
            profile = await db.Profiles.FirstOrDefaultAsync(p => p.Id == pid, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            profile = new Profile
            {
                Name = preset.Name,
                DirPath = $"{ActivationPaths.SwitchDirName}/{preset.Name}", // relative — resolved against vamRoot on use
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Profiles.Add(profile);
            await SaveAsync(cancellationToken).ConfigureAwait(false);

            preset.ProfileId = profile.Id;
            preset.UpdatedAt = now;
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        // Ensure the on-disk profile + link folders exist (idempotent).
        Directory.CreateDirectory(ActivationPaths.ProfileDir(vamRoot, profile.Name));
        Directory.CreateDirectory(ActivationPaths.VarsLinkDir(vamRoot, profile.Name));
        Directory.CreateDirectory(ActivationPaths.MissingVarLinkDir(vamRoot, profile.Name));
        return profile;
    }

    private Task SaveAsync(CancellationToken cancellationToken) =>
        writeQueue.EnqueueAsync(ct => db.SaveChangesAsync(ct), WritePriority.Interactive, cancellationToken);
}
