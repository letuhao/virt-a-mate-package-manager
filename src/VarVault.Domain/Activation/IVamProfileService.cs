using VarVault.Common;

namespace VarVault.Domain.Activation;

/// <summary>
/// Loading-profile switching for a VaM install. Each profile is a real directory under
/// <c>___AddonPacksSwitch ___</c>; VaM's <c>AddonPackages</c> is a single directory symlink that points
/// at the active profile. Switching profiles repoints that one link — O(1), independent of how many
/// vars a profile holds (no per-var work). (Checklist 3.1/3.2; data-arch §5.7.)
/// </summary>
public interface IVamProfileService
{
    /// <summary>The <c>___AddonPacksSwitch ___</c> directory that holds every profile, for a VaM root.</summary>
    string SwitchRoot(string vamRoot);

    /// <summary>The <c>AddonPackages</c> path — the directory symlink VaM reads packages from.</summary>
    string AddonPackagesLink(string vamRoot);

    /// <summary>The backing directory for a named profile under the switch root.</summary>
    string ProfileDirectory(string vamRoot, string profileName);

    /// <summary>Profile names present under the switch root (the directories that can be switched to).</summary>
    Result<IReadOnlyList<string>> ListProfiles(string vamRoot);

    /// <summary>Create an empty profile directory (no-op if it already exists). Rejects path-bearing names.</summary>
    Result CreateProfile(string vamRoot, string profileName);

    /// <summary>The profile <c>AddonPackages</c> currently points at, or null if unset / not a managed link.</summary>
    string? ActiveProfile(string vamRoot);

    /// <summary>
    /// Switch the active loading profile by repointing the single <c>AddonPackages</c> symlink at the
    /// named profile. The profile directory must exist. This is the flat-cost preset switch. (3.2)
    /// </summary>
    Result SwitchTo(string vamRoot, string profileName);

    /// <summary>Delete a profile directory (and its own links — never their targets). Refuses the active profile. (doc 26 · G-6)</summary>
    Result DeleteProfile(string vamRoot, string profileName);

    /// <summary>Rename a profile directory; repoints <c>AddonPackages</c> if it was the active profile. (doc 26 · G-6)</summary>
    Result RenameProfile(string vamRoot, string oldName, string newName);
}
