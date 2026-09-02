using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Common;
using VarVault.Domain.Activation;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Settings;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N7 · Profile facade. Resolves the VaM root from settings and delegates to
/// <see cref="IVamProfileService"/> (list/create/active/switch). Switching repoints one symlink. (BE-N7.)
/// </summary>
public sealed class EfProfileService(
    IVamProfileService profiles,
    ISettingsService settings,
    IProfilePackageLinkService profileLinks,
    IWriteQueue writeQueue) : IProfileService
{
    private async Task<Result<string>> VamRootAsync(CancellationToken cancellationToken)
    {
        var root = await settings.GetAsync(SettingKeys.VamPath, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(root)
            ? Result.Failure<string>("profile.novamroot", "VaM install path is not set.")
            : Result.Success(root);
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        var root = await VamRootAsync(cancellationToken).ConfigureAwait(false);
        if (root.IsFailure)
            return [];
        var listed = profiles.ListProfiles(root.Value);
        return listed.IsSuccess ? listed.Value : [];
    }

    public async Task<string?> ActiveAsync(CancellationToken cancellationToken = default)
    {
        var root = await VamRootAsync(cancellationToken).ConfigureAwait(false);
        return root.IsSuccess ? profiles.ActiveProfile(root.Value) : null;
    }

    public async Task<Result> CreateAsync(string profileName, CancellationToken cancellationToken = default)
    {
        var root = await VamRootAsync(cancellationToken).ConfigureAwait(false);
        return root.IsFailure ? Result.Failure(root.Error.Code, root.Error.Message) : profiles.CreateProfile(root.Value, profileName);
    }

    public async Task<Result> SwitchToAsync(string profileName, CancellationToken cancellationToken = default)
    {
        var root = await VamRootAsync(cancellationToken).ConfigureAwait(false);
        if (root.IsFailure)
            return Result.Failure(root.Error.Code, root.Error.Message);
        var switched = profiles.SwitchTo(root.Value, profileName);
        if (switched.IsFailure)
            return switched;

        await writeQueue.EnqueueScopedAsync(async (sp, ct) =>
        {
            var db = sp.GetRequiredService<VarVaultDbContext>();
            var all = await db.Profiles.ToListAsync(ct).ConfigureAwait(false);
            foreach (var p in all)
                p.IsActive = string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, WritePriority.Interactive, cancellationToken).ConfigureAwait(false);

        await profileLinks.RefreshActiveProfileReadModelAsync(cancellationToken).ConfigureAwait(false);
        return switched;
    }

    public async Task<Result> DeleteAsync(string profileName, CancellationToken cancellationToken = default)
    {
        var root = await VamRootAsync(cancellationToken).ConfigureAwait(false);
        return root.IsFailure ? Result.Failure(root.Error.Code, root.Error.Message) : profiles.DeleteProfile(root.Value, profileName);
    }

    public async Task<Result> RenameAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        var root = await VamRootAsync(cancellationToken).ConfigureAwait(false);
        if (root.IsFailure)
            return Result.Failure(root.Error.Code, root.Error.Message);
        var renamed = profiles.RenameProfile(root.Value, oldName, newName);
        if (renamed.IsFailure)
            return renamed;

        var becameActive = false;
        await writeQueue.EnqueueScopedAsync(async (sp, ct) =>
        {
            var db = sp.GetRequiredService<VarVaultDbContext>();
            var profile = await db.Profiles.FirstOrDefaultAsync(p => p.Name == oldName, ct).ConfigureAwait(false);
            if (profile is null)
                return;
            profile.Name = newName;
            profile.IsActive = string.Equals(profiles.ActiveProfile(root.Value), newName, StringComparison.OrdinalIgnoreCase);
            becameActive = profile.IsActive;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, WritePriority.Interactive, cancellationToken).ConfigureAwait(false);

        if (becameActive)
            await profileLinks.RefreshActiveProfileReadModelAsync(cancellationToken).ConfigureAwait(false);
        return renamed;
    }
}
