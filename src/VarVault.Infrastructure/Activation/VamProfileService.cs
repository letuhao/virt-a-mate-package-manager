using System.IO;
using VarVault.Common;
using VarVault.Domain.Activation;

namespace VarVault.Infrastructure.Activation;

/// <summary>
/// Filesystem implementation of loading-profile switching. Profile directories live under
/// <c>___AddonPacksSwitch ___</c>; the active one is selected by repointing the <c>AddonPackages</c>
/// directory symlink via <see cref="ISymlinkService"/> — a single link operation, never a per-var walk.
/// (Checklist 3.1/3.2.)
/// </summary>
public sealed class VamProfileService(ISymlinkService symlinks) : IVamProfileService
{
    // Load-bearing directory name (verbatim, incl. the space before the trailing underscores).
    private const string SwitchDirName = "___AddonPacksSwitch ___";
    private const string AddonPackagesName = "AddonPackages";

    public string SwitchRoot(string vamRoot)
    {
        Guard.NotNullOrWhiteSpace(vamRoot);
        return Path.Combine(vamRoot, SwitchDirName);
    }

    public string AddonPackagesLink(string vamRoot)
    {
        Guard.NotNullOrWhiteSpace(vamRoot);
        return Path.Combine(vamRoot, AddonPackagesName);
    }

    public string ProfileDirectory(string vamRoot, string profileName)
    {
        Guard.NotNullOrWhiteSpace(profileName);
        return Path.Combine(SwitchRoot(vamRoot), profileName);
    }

    public Result<IReadOnlyList<string>> ListProfiles(string vamRoot)
    {
        var root = SwitchRoot(vamRoot);
        if (!Directory.Exists(root))
            return Result<IReadOnlyList<string>>.Success([]);
        try
        {
            IReadOnlyList<string> names = Directory.EnumerateDirectories(root)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Result<IReadOnlyList<string>>.Success(names);
        }
        catch (IOException ex)
        {
            return Result<IReadOnlyList<string>>.Failure("profile.list", ex.Message);
        }
    }

    public Result CreateProfile(string vamRoot, string profileName)
    {
        if (!IsValidName(profileName))
            return Result.Failure("profile.name", "Profile name must not contain path separators.");
        try
        {
            Directory.CreateDirectory(ProfileDirectory(vamRoot, profileName));
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failure("profile.create", ex.Message);
        }
    }

    public string? ActiveProfile(string vamRoot)
    {
        var link = AddonPackagesLink(vamRoot);
        if (!symlinks.IsLink(link))
            return null;
        var target = symlinks.ResolveTarget(link);
        return target is null ? null : Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    public Result SwitchTo(string vamRoot, string profileName)
    {
        if (!IsValidName(profileName))
            return Result.Failure("profile.name", "Profile name must not contain path separators.");

        var profileDir = ProfileDirectory(vamRoot, profileName);
        if (!Directory.Exists(profileDir))
            return Result.Failure("profile.missing", $"Profile '{profileName}' does not exist.");

        // The whole switch: repoint one symlink. No enumeration of the profile's contents. (3.2)
        return symlinks.RepointDirectory(AddonPackagesLink(vamRoot), profileDir);
    }

    private static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0
        && name is not ("." or "..");
}
