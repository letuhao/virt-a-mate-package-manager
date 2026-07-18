using System.IO;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Filesystem implementation of <see cref="IRepositoryEnumerator"/>. A manual, stack-based walk so it
/// can skip reparse points (never index a symlinked var or descend a junction) and tag quarantine
/// buckets by directory prefix. Inaccessible directories are skipped, not fatal. (IDX-1, 1.11/1.29.)
/// </summary>
public sealed class RepositoryEnumerator : IRepositoryEnumerator
{
    public IEnumerable<ScannedVar> Enumerate(
        string repositoryRoot,
        bool includeQuarantined = true,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
            yield break;

        var stack = new Stack<(string Dir, QuarantineKind Quarantine)>();
        stack.Push((root, QuarantineKind.None));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (dir, quarantine) = stack.Pop();

            IReadOnlyList<string> files;
            IReadOnlyList<string> subdirs;
            try
            {
                files = Directory.GetFiles(dir, "*.var");
                subdirs = Directory.GetDirectories(dir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                continue; // skip inaccessible directory
            }

            foreach (var file in files)
            {
                if (!includeQuarantined && quarantine != QuarantineKind.None)
                    continue;

                var info = SafeFileInfo(file);
                if (info is null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue; // never index a symlinked var

                yield return new ScannedVar(
                    file,
                    Path.GetRelativePath(root, file),
                    info.Length,
                    info.LastWriteTimeUtc,
                    quarantine);
            }

            foreach (var sub in subdirs)
            {
                var name = Path.GetFileName(sub);
                if (RepositoryScanRules.IsLinkDirectory(name))
                    continue;

                var subInfo = SafeDirInfo(sub);
                if (subInfo is null || subInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue; // don't descend junctions/symlinked dirs

                var childQuarantine = quarantine != QuarantineKind.None
                    ? quarantine
                    : RepositoryScanRules.ClassifyDirectory(name);
                stack.Push((sub, childQuarantine));
            }
        }
    }

    private static FileInfo? SafeFileInfo(string path)
    {
        try { return new FileInfo(path); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return null; }
    }

    private static DirectoryInfo? SafeDirInfo(string path)
    {
        try { return new DirectoryInfo(path); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return null; }
    }
}
