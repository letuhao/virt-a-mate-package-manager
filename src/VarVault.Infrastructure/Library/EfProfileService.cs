using VarVault.Common;
using VarVault.Domain.Activation;
using VarVault.Sdk.Library;
using VarVault.Sdk.Settings;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N7 · Profile facade. Resolves the VaM root from settings and delegates to
/// <see cref="IVamProfileService"/> (list/create/active/switch). Switching repoints one symlink. (BE-N7.)
/// </summary>
public sealed class EfProfileService(IVamProfileService profiles, ISettingsService settings) : IProfileService
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
        return root.IsFailure ? Result.Failure(root.Error.Code, root.Error.Message) : profiles.SwitchTo(root.Value, profileName);
    }

    public async Task<Result> DeleteAsync(string profileName, CancellationToken cancellationToken = default)
    {
        var root = await VamRootAsync(cancellationToken).ConfigureAwait(false);
        return root.IsFailure ? Result.Failure(root.Error.Code, root.Error.Message) : profiles.DeleteProfile(root.Value, profileName);
    }

    public async Task<Result> RenameAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        var root = await VamRootAsync(cancellationToken).ConfigureAwait(false);
        return root.IsFailure ? Result.Failure(root.Error.Code, root.Error.Message) : profiles.RenameProfile(root.Value, oldName, newName);
    }
}
