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
    private const string SwitchDirName = ActivationPaths.SwitchDirName;
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

    public Result DeleteProfile(string vamRoot, string profileName)
    {
        if (!IsValidName(profileName))
            return Result.Failure("profile.name", "Profile name must not contain path separators.");
        var dir = ProfileDirectory(vamRoot, profileName);
        if (!Directory.Exists(dir))
            return Result.Success(); // idempotent
        if (string.Equals(ActiveProfile(vamRoot), profileName, StringComparison.OrdinalIgnoreCase))
            return Result.Failure("profile.active", "Cannot delete the active profile; switch away first.");
        try
        {
            // Recursive delete removes the profile's own symlinks — never their targets (.NET 6+ does not
            // traverse reparse points on delete). (doc 26 · G-6)
            Directory.Delete(dir, recursive: true);
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure("profile.delete", ex.Message);
        }
    }

    public Result RenameProfile(string vamRoot, string oldName, string newName)
    {
        if (!IsValidName(oldName) || !IsValidName(newName))
            return Result.Failure("profile.name", "Profile name must not contain path separators.");
        var oldDir = ProfileDirectory(vamRoot, oldName);
        var newDir = ProfileDirectory(vamRoot, newName);
        if (!Directory.Exists(oldDir))
            return Result.Failure("profile.missing", $"Profile '{oldName}' does not exist.");
        if (Directory.Exists(newDir))
            return Result.Failure("profile.exists", $"Profile '{newName}' already exists.");
        var wasActive = string.Equals(ActiveProfile(vamRoot), oldName, StringComparison.OrdinalIgnoreCase);
        try
        {
            Directory.Move(oldDir, newDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure("profile.rename", ex.Message);
        }
        return wasActive ? symlinks.RepointDirectory(AddonPackagesLink(vamRoot), newDir) : Result.Success();
    }

    private static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0
        && name is not ("." or "..");
}
