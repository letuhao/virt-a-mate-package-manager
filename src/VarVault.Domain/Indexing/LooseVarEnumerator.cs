using System.IO;
using VarVault.Common;

namespace VarVault.Domain.Indexing;

/// <summary>
/// Enumerate real (non-symlink) <c>.var</c> / archive files under a folder for Import / AddonPackages tidy,
/// skipping the app's link-farm directories and reparse points. Same spirit as legacy
/// <c>GetAddonpackagesVars</c> and <see cref="RepositoryEnumerator"/>.
/// </summary>
public static class LooseVarEnumerator
{
    private static readonly string[] ArchiveExtensions = [".zip", ".7z", ".rar", ".tar"];

    /// <summary>Absolute paths of real <c>*.var</c> files under <paramref name="root"/> (recursive, link-safe).</summary>
    public static IEnumerable<string> EnumerateVarFiles(string root, CancellationToken cancellationToken = default)
    {
        foreach (var file in WalkFiles(root, "*.var", cancellationToken))
            yield return file;
    }

    /// <summary>Absolute paths of nested archives under <paramref name="root"/> (recursive, link-safe).</summary>
    public static IEnumerable<string> EnumerateArchiveFiles(string root, CancellationToken cancellationToken = default)
    {
        foreach (var file in WalkFiles(root, "*.*", cancellationToken))
        {
            if (IsArchive(file))
                yield return file;
        }
    }

    public static int CountVarFiles(string root, CancellationToken cancellationToken = default) =>
        EnumerateVarFiles(root, cancellationToken).Count();

    /// <summary>True when <paramref name="path"/> is a real file (not a reparse point / symlink).</summary>
    public static bool IsRealFile(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.Directory) == 0
                   && (attrs & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when <paramref name="filePath"/> sits under a link-farm directory segment
    /// (<c>___VarsLink___</c> etc.) relative to <paramref name="root"/>.
    /// </summary>
    public static bool IsUnderLinkDirectory(string root, string filePath)
    {
        var relative = Path.GetRelativePath(root, filePath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return true; // outside root — refuse
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (RepositoryScanRules.IsLinkDirectory(segment))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> WalkFiles(string root, string pattern, CancellationToken cancellationToken)
    {
        Guard.NotNullOrWhiteSpace(root);
        if (!Directory.Exists(root))
            yield break;

        var stack = new Stack<string>();
        stack.Push(Path.GetFullPath(root));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            string[] files;
            string[] subdirs;
            try
            {
                files = Directory.GetFiles(dir, pattern);
                subdirs = Directory.GetDirectories(dir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!IsRealFile(file))
                    continue;
                yield return file;
            }

            foreach (var sub in subdirs)
            {
                var name = Path.GetFileName(sub);
                if (RepositoryScanRules.IsLinkDirectory(name))
                    continue;
                try
                {
                    var attrs = File.GetAttributes(sub);
                    if ((attrs & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
                {
                    continue;
                }
                stack.Push(sub);
            }
        }
    }

    private static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path);
        foreach (var a in ArchiveExtensions)
        {
            if (ext.Equals(a, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="directoryName"/> is a per-profile symlink farm
    /// (<c>___VarsLink___</c> / <c>___MissingVarLink___</c> / <c>___TempVarLink___</c>).
    /// Does <b>not</b> include <c>___AddonPacksSwitch ___</c> — profile roots live under that dir
    /// and loose originals there are valid tidy targets.
    /// </summary>
    public static bool IsSymlinkFarmDirectory(string directoryName) =>
        directoryName.StartsWith("___VarsLink___", StringComparison.OrdinalIgnoreCase)
        || directoryName.StartsWith("___MissingVarLink___", StringComparison.OrdinalIgnoreCase)
        || directoryName.StartsWith("___TempVarLink___", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when any path segment is a symlink-farm directory (not AddonPacksSwitch).</summary>
    public static bool PathContainsSymlinkFarm(string path)
    {
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (IsSymlinkFarmDirectory(segment))
                return true;
        }
        return false;
    }
}
