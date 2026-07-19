using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// EF activation link builder: member closure → hottest online copy per package → ActivationLink rows.
/// (Checklist 3.4/3.5/3.7.)
/// </summary>
public sealed class EfActivationService(VarVaultDbContext db, IClock clock, IDependencyGraph graph) : IActivationService
{
    public async Task<ActivationBuildResult> BuildProfileLinksAsync(long presetId, CancellationToken cancellationToken = default)
    {
        var active = await ResolvedMemberIdsAsync(presetId, cancellationToken).ConfigureAwait(false);
        return await RecomputeAsync(presetId, active, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActivationBuildResult> DeactivateAsync(long presetId, long packageId, CancellationToken cancellationToken = default)
    {
        var active = await ResolvedMemberIdsAsync(presetId, cancellationToken).ConfigureAwait(false);
        active.Remove(packageId); // ref-counting: deps stay if another active member still needs them
        return await RecomputeAsync(presetId, active, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RescueAsync(long profileId, CancellationToken cancellationToken = default)
    {
        // Remove every app-created link (a preset owns it); user-made links (null attribution) survive.
        return await db.ActivationLinks
            .Where(l => l.ProfileId == profileId && l.RequestedByPresetId != null)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CleanTempLinksAsync(long profileId, CancellationToken cancellationToken = default)
    {
        return await db.ActivationLinks
            .Where(l => l.ProfileId == profileId && l.LinkKind == LinkKind.Temp)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<HashSet<long>> ResolvedMemberIdsAsync(long presetId, CancellationToken cancellationToken)
    {
        var members = await db.PresetMembers.AsNoTracking()
            .Where(m => m.PresetId == presetId)
            .Select(m => new { m.ResolvedPackageId, m.PackageRefKey })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var ids = new HashSet<long>();
        var unresolvedKeys = new List<string>();
        foreach (var m in members)
        {
            if (m.ResolvedPackageId is { } id)
                ids.Add(id);
            else
                unresolvedKeys.Add(m.PackageRefKey);
        }

        // Persistent aliases (global + per-preset) re-apply automatically every build — no re-setup. (3.9)
        if (unresolvedKeys.Count > 0)
        {
            var aliased = await db.VarAliases.AsNoTracking()
                .Where(a => a.ResolvedPackageId != null
                            && unresolvedKeys.Contains(a.MissingRefKey)
                            && (a.Scope == AliasScope.Global || a.PresetId == presetId))
                .Select(a => a.ResolvedPackageId!.Value)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var id in aliased)
                ids.Add(id);
        }

        return ids;
    }

    private async Task<ActivationBuildResult> RecomputeAsync(long presetId, HashSet<long> directSet, CancellationToken cancellationToken)
    {
        var preset = await db.LoadingPresets.FirstOrDefaultAsync(p => p.Id == presetId, cancellationToken).ConfigureAwait(false);
        if (preset is null)
            return new ActivationBuildResult(0, 0);

        var now = clock.UtcNow.UtcDateTime;
        var profile = await EnsureProfileAsync(preset, now, cancellationToken).ConfigureAwait(false);

        var full = new HashSet<long>(directSet);
        foreach (var id in directSet)
        {
            foreach (var dep in await graph.ForwardClosureAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false))
                full.Add(dep);
        }

        // Replace only the links THIS preset created (owned); never touch user-made links. (3.12)
        var existing = await db.ActivationLinks
            .Where(l => l.ProfileId == profile.Id && l.RequestedByPresetId == presetId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
            db.ActivationLinks.RemoveRange(existing);

        var created = 0;
        var missing = 0;
        foreach (var packageId in full)
        {
            var varFileId = await PickHottestOnlineCopyAsync(packageId, cancellationToken).ConfigureAwait(false);
            if (varFileId is null)
            {
                missing++; // no online copy to link
                continue;
            }

            db.ActivationLinks.Add(new ActivationLink
            {
                ProfileId = profile.Id,
                VarFileId = varFileId.Value,
                LinkPath = $"{profile.DirPath}/___VarsLink___/{packageId}.var",
                LinkKind = LinkKind.Install,
                LinkType = LinkType.Symlink,
                Reason = directSet.Contains(packageId) ? ActivationReason.Explicit : ActivationReason.DependencyOf,
                RequestedByPresetId = presetId,
            });
            created++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ActivationBuildResult(created, missing);
    }

    // The hottest online copy = the VarFile in the lowest-tier online repository. (3.4)
    private async Task<long?> PickHottestOnlineCopyAsync(long packageId, CancellationToken cancellationToken)
    {
        return await (
            from v in db.VarFiles
            join r in db.Repositories on v.RepositoryId equals r.Id
            where v.PackageId == packageId && r.IsOnline && r.IsEnabled
            orderby r.Tier, r.PriorityInTier, v.Id
            select (long?)v.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Profile> EnsureProfileAsync(LoadingPreset preset, DateTime now, CancellationToken cancellationToken)
    {
        if (preset.ProfileId is { } pid)
        {
            var existing = await db.Profiles.FirstOrDefaultAsync(p => p.Id == pid, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
                return existing;
        }

        var profile = new Profile
        {
            Name = preset.Name,
            DirPath = $"___AddonPacksSwitch ___/{preset.Name}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        preset.ProfileId = profile.Id;
        preset.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return profile;
    }
}
