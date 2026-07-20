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

    /// <summary>Absolute path of a source var in its repository = repository mount + relative path.</summary>
    public static string SourcePath(string repositoryMountPath, string relativePath)
    {
        Guard.NotNullOrWhiteSpace(repositoryMountPath);
        Guard.NotNullOrWhiteSpace(relativePath);
        return Path.Combine(repositoryMountPath, relativePath);
    }
}
