using System.IO;
using VarVault.Common;

namespace VarVault.Infrastructure.Persistence;

/// <summary>
/// ⚠ Safety guard (data-arch §5.9, §7): the catalog DB must live on <b>stable local storage</b>.
/// It refuses a path on a removable/network volume, or one that sits inside a registered
/// repository (the DB is safety infrastructure and must not travel or vanish with the vars).
/// </summary>
public static class CatalogLocationGuard
{
    /// <summary>
    /// Validate a candidate database path against drive type and repository overlap.
    /// </summary>
    /// <param name="databasePath">The catalog DB file path.</param>
    /// <param name="repositoryPaths">Registered repository mount paths (may be empty at first run).</param>
    /// <param name="driveTypeResolver">
    /// Seam for testability; defaults to <see cref="DriveInfo"/>. Given a path, returns the drive type.
    /// </param>
    public static Result Validate(
        string databasePath,
        IEnumerable<string> repositoryPaths,
        Func<string, DriveType>? driveTypeResolver = null)
    {
        Guard.NotNullOrWhiteSpace(databasePath);
        Guard.NotNull(repositoryPaths);

        var fullDbPath = Path.GetFullPath(databasePath);
        var dbDir = Path.GetDirectoryName(fullDbPath) ?? fullDbPath;

        var resolve = driveTypeResolver ?? DefaultDriveType;
        var driveType = resolve(dbDir);
        if (driveType is not DriveType.Fixed)
        {
            return Result.Failure(
                "catalog.location.volume",
                $"The catalog database must live on a fixed local drive, but '{dbDir}' is on a {driveType} volume.");
        }

        foreach (var repoPath in repositoryPaths)
        {
            if (string.IsNullOrWhiteSpace(repoPath))
                continue;
            if (IsSameOrNested(dbDir, Path.GetFullPath(repoPath)))
            {
                return Result.Failure(
                    "catalog.location.repo",
                    $"The catalog database at '{dbDir}' must not live inside repository '{repoPath}'.");
            }
        }

        return Result.Success();
    }

    /// <summary>True if <paramref name="candidate"/> equals or is nested within <paramref name="ancestor"/> (either direction).</summary>
    private static bool IsSameOrNested(string candidate, string ancestor)
    {
        var a = NormalizeDir(candidate);
        var b = NormalizeDir(ancestor);
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
            return true;
        // Append a separator only when absent, so a root ("E:\") doesn't become "E:\\".
        var aSep = a.EndsWith(Path.DirectorySeparatorChar) ? a : a + Path.DirectorySeparatorChar;
        var bSep = b.EndsWith(Path.DirectorySeparatorChar) ? b : b + Path.DirectorySeparatorChar;
        return aSep.StartsWith(bSep, StringComparison.OrdinalIgnoreCase)
            || bSep.StartsWith(aSep, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDir(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static DriveType DefaultDriveType(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? DriveType.Unknown : new DriveInfo(root).DriveType;
        }
        catch (ArgumentException)
        {
            return DriveType.Unknown;
        }
        catch (IOException)
        {
            return DriveType.Unknown;
        }
    }
}
