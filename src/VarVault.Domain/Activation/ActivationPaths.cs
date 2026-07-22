using System.IO;
using VarVault.Common;
using VarVault.Domain.ValueObjects;

namespace VarVault.Domain.Activation;

/// <summary>
/// Load-bearing path/identity rules for on-disk activation links. Centralizes the special directory
/// names (verbatim — including the space in <c>___AddonPacksSwitch ___</c>) and the VaM identity
/// filename rule so the activation builder, the profile service, and tests agree on one source of truth.
/// (Spec 21 §2/§3.4; checklist 22 · T2.1.)
/// </summary>
public static class ActivationPaths
{
    /// <summary>Profiles root under the VaM install. The trailing space before "___" is literal — do not change.</summary>
    public const string SwitchDirName = "___AddonPacksSwitch ___";

    /// <summary>Per-var install links live here inside a profile.</summary>
    public const string VarsLinkDirName = "___VarsLink___";

    /// <summary>Alias links (missing ref → owned var) live here inside a profile.</summary>
    public const string MissingVarLinkDirName = "___MissingVarLink___";

    public static string SwitchRoot(string vamRoot)
    {
        Guard.NotNullOrWhiteSpace(vamRoot);
        return Path.Combine(vamRoot, SwitchDirName);
    }

    public static string ProfileDir(string vamRoot, string profileName)
    {
        Guard.NotNullOrWhiteSpace(profileName);
        return Path.Combine(SwitchRoot(vamRoot), profileName);
    }

    public static string VarsLinkDir(string vamRoot, string profileName) =>
        Path.Combine(ProfileDir(vamRoot, profileName), VarsLinkDirName);

    public static string MissingVarLinkDir(string vamRoot, string profileName) =>
        Path.Combine(ProfileDir(vamRoot, profileName), MissingVarLinkDirName);

    /// <summary>
    /// The VaM-correct link filename for a package identity. This IS the identity VaM keys on, so it must be
    /// the verbatim <c>Creator.Package.Version.var</c> (e.g. <c>.007</c> preserved) — never the numeric DB id,
    /// never the fold key. Rejects malformed names so a link VaM can't parse is never written.
    /// </summary>
    public static Result<string> LinkFileName(string varName)
    {
        var parsed = PackageId.TryParse(varName);
        return parsed.IsFailure
            ? Result.Failure<string>(parsed.Error.Code, parsed.Error.Message)
            : Result.Success($"{parsed.Value.VarName}.var");
    }

    /// <summary>
    /// Alias link filename under <c>___MissingVarLink___</c> (legacy <c>FormMissingVars.Createlink</c>).
    /// Numeric missing refs keep their verbatim name; <c>.latest</c> is rewritten to the owned target's
    /// version token so VaM can parse the link name.
    /// </summary>
    public static Result<string> AliasLinkFileName(string missingRefRaw, string targetVarName)
    {
        Guard.NotNullOrWhiteSpace(missingRefRaw);
        Guard.NotNullOrWhiteSpace(targetVarName);

        var missing = missingRefRaw.EndsWith(".var", StringComparison.OrdinalIgnoreCase)
            ? missingRefRaw[..^4]
            : missingRefRaw;
        var lastDot = missing.LastIndexOf('.');
        if (lastDot > 0 && string.Equals(missing[(lastDot + 1)..], "latest", StringComparison.OrdinalIgnoreCase))
        {
            var target = PackageId.TryParse(targetVarName);
            if (target.IsFailure)
                return Result.Failure<string>(target.Error.Code, target.Error.Message);
            return Result.Success($"{missing[..lastDot]}.{target.Value.VersionToken}.var");
        }

        return LinkFileName(missing);
    }

    /// <summary>Absolute path of a source var in its repository = repository mount + relative path.</summary>
    public static string SourcePath(string repositoryMountPath, string relativePath)
    {
        Guard.NotNullOrWhiteSpace(repositoryMountPath);
        Guard.NotNullOrWhiteSpace(relativePath);
        return Path.Combine(repositoryMountPath, relativePath);
    }
}
