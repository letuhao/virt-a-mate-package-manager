using System.IO;
using VarVault.Domain.Activation;
using VarVault.Domain.Indexing;
using VarVault.Sdk.Import;
using VarVault.Sdk.Library;
using VarVault.Sdk.Settings;

namespace VarVault.Infrastructure.Library;

/// <summary>Active profile path + loose-var count for Import Quick import (TidyVars-style).</summary>
public sealed class AddonPackagesLooseVarsLocator(
    ISettingsService settings,
    IProfileService profiles) : IAddonPackagesLooseVarsLocator
{
    public async Task<LooseVarsLocateResult> LocateAsync(CancellationToken cancellationToken = default)
    {
        var vamRoot = await settings.GetAsync(SettingKeys.VamPath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(vamRoot))
        {
            return new LooseVarsLocateResult(
                LooseVarsLocateStatus.NoVamPath,
                null, null, 0,
                "Set VaM path in Settings — in-game downloads land in the active profile folder.");
        }

        var active = await profiles.ActiveAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(active))
        {
            return new LooseVarsLocateResult(
                LooseVarsLocateStatus.NoActiveProfile,
                null, null, 0,
                "No active VaM profile — switch a profile (or create one), then retry Quick import.");
        }

        var profilePath = ActivationPaths.ProfileDir(vamRoot, active);
        if (!Directory.Exists(profilePath))
        {
            return new LooseVarsLocateResult(
                LooseVarsLocateStatus.ProfileMissing,
                profilePath, active, 0,
                $"Profile folder missing: {profilePath}");
        }

        var count = LooseVarEnumerator.CountVarFiles(profilePath, cancellationToken);
        var hint = count == 0
            ? $"No loose .var files in “{active}” (symlink farms are skipped)."
            : $"{count} loose var{(count == 1 ? "" : "s")} in “{active}” — Quick import skips ___VarsLink___ / aliases.";
        return new LooseVarsLocateResult(LooseVarsLocateStatus.Ready, profilePath, active, count, hint);
    }
}
