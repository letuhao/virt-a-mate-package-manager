using VarVault.Common;

namespace VarVault.Domain.Activation;

/// <summary>
/// Filesystem link operations behind activation. The load-bearing one is <see cref="RepointDirectory"/>:
/// switching a loading preset is just repointing the single <c>AddonPackages</c> directory symlink —
/// O(1) regardless of how many vars the profile contains. Implemented by Infrastructure; requires
/// Windows Developer Mode (or elevation) to create links, else a clear error. (Data-arch §5.7; 3.2/3.3.)
/// </summary>
public interface ISymlinkService
{
    /// <summary>Create a directory symlink at <paramref name="linkPath"/> pointing at <paramref name="targetPath"/>.</summary>
    Result CreateDirectory(string linkPath, string targetPath);

    /// <summary>Create a file symlink.</summary>
    Result CreateFile(string linkPath, string targetPath);

    /// <summary>
    /// Atomically repoint an existing directory symlink to a new target (removing the old link, not its
    /// target). This is the instant preset switch. (3.2)
    /// </summary>
    Result RepointDirectory(string linkPath, string newTargetPath);

    /// <summary>The target a link points at, or null if the path isn't a link.</summary>
    string? ResolveTarget(string linkPath);

    /// <summary>True if the path is a reparse point (symlink/junction).</summary>
    bool IsLink(string path);
}
