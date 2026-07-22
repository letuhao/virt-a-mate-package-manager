using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// Shared “add these var names to the active loading preset + rebuild links” path used by VaM-log repair
/// and installed-deps repair. Never invents a disposable repair preset.
/// </summary>
internal sealed class ActivePresetActivationHelper(
    VarVaultDbContext db,
    IPresetService presets,
    IActivationService activation,
    IProfileService profiles)
{
    public async Task<MissingLogActivation> ActivateAsync(
        IReadOnlyList<string> varNames,
        CancellationToken cancellationToken = default)
    {
        var members = varNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (members.Count == 0)
            return new MissingLogActivation(0, 0, 0, 0);

        var target = await ResolveActivePresetAsync(cancellationToken).ConfigureAwait(false);
        if (target is null)
            return new MissingLogActivation(0, 0, 0, 0);

        var ensured = 0;
        foreach (var name in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await presets.AddMemberAsync(target.Value, name, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
                ensured++;
        }

        var build = await activation.BuildProfileLinksAsync(target.Value, cancellationToken).ConfigureAwait(false);
        return new MissingLogActivation(
            MembersActivated: ensured,
            LinksCreated: build.LinksCreated,
            StillMissing: build.MissingPackages,
            PrivilegeFailures: build.PrivilegeFailures,
            UnresolvedDependencies: build.UnresolvedDependencies,
            PathUnavailable: build.PathUnavailable);
    }

    /// <summary>Rebuild profile links for the active loading preset without adding members (alias materialization).</summary>
    public async Task<MissingLogActivation> RebuildActiveAsync(CancellationToken cancellationToken = default)
    {
        var target = await ResolveActivePresetAsync(cancellationToken).ConfigureAwait(false);
        if (target is null)
            return new MissingLogActivation(0, 0, 0, 0);

        var build = await activation.BuildProfileLinksAsync(target.Value, cancellationToken).ConfigureAwait(false);
        return new MissingLogActivation(
            MembersActivated: 0,
            LinksCreated: build.LinksCreated,
            StillMissing: build.MissingPackages,
            PrivilegeFailures: build.PrivilegeFailures,
            UnresolvedDependencies: build.UnresolvedDependencies,
            PathUnavailable: build.PathUnavailable);
    }

    private async Task<long?> ResolveActivePresetAsync(CancellationToken cancellationToken)
    {
        var activeName = await profiles.ActiveAsync(cancellationToken).ConfigureAwait(false);

        Profile? profile = null;
        if (!string.IsNullOrWhiteSpace(activeName))
        {
            profile = await db.Profiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Name == activeName, cancellationToken)
                .ConfigureAwait(false);
        }
        profile ??= await db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (profile is not null)
        {
            var linked = await db.LoadingPresets.AsNoTracking()
                .Where(p => p.ProfileId == profile.Id)
                .OrderByDescending(p => p.UpdatedAt)
                .Select(p => (long?)p.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (linked is not null)
                return linked;

            return await EnsurePresetLinkedToProfileAsync(profile.Id, profile.Name, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(activeName))
        {
            var byName = await db.LoadingPresets.AsNoTracking()
                .Where(p => p.Name == activeName)
                .Select(p => (long?)p.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (byName is not null)
                return byName;

            var created = await presets.CreateAsync(activeName, [], cancellationToken).ConfigureAwait(false);
            return created.IsSuccess ? created.Value.Id : null;
        }

        var any = await db.LoadingPresets.AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => (long?)p.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (any is not null)
            return any;

        var createdDefault = await presets.CreateAsync("Default", [], cancellationToken).ConfigureAwait(false);
        return createdDefault.IsSuccess ? createdDefault.Value.Id : null;
    }

    private async Task<long?> EnsurePresetLinkedToProfileAsync(long profileId, string profileName, CancellationToken cancellationToken)
    {
        var existing = await db.LoadingPresets
            .FirstOrDefaultAsync(p => p.Name == profileName, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ProfileId != profileId)
            {
                existing.ProfileId = profileId;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            return existing.Id;
        }

        var created = await presets.CreateAsync(profileName, [], cancellationToken).ConfigureAwait(false);
        if (created.IsFailure)
            return null;

        var preset = await db.LoadingPresets.FirstOrDefaultAsync(p => p.Id == created.Value.Id, cancellationToken).ConfigureAwait(false);
        if (preset is null)
            return null;
        preset.ProfileId = profileId;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return preset.Id;
    }
}
