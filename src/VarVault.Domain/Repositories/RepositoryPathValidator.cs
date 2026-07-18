using System.IO;
using VarVault.Common;

namespace VarVault.Domain.Repositories;

/// <summary>
/// 🔒 Validates a candidate repository path: it must not equal or nest within
/// <c>{vampath}\AddonPackages</c> (the app manages that via symlinks) and must not overlap an
/// already-registered repository (either direction). Pure. (Checklist 1.2.)
/// </summary>
public static class RepositoryPathValidator
{
    public static Result Validate(
        string candidatePath,
        string? addonPackagesPath,
        IEnumerable<string> existingRepositoryPaths)
    {
        Guard.NotNullOrWhiteSpace(candidatePath);
        Guard.NotNull(existingRepositoryPaths);

        var candidate = Normalize(candidatePath);

        if (!string.IsNullOrWhiteSpace(addonPackagesPath) &&
            IsSameOrNested(candidate, Normalize(addonPackagesPath)))
        {
            return Result.Failure(
                "repo.path.addonpackages",
                "A repository must not be the game's AddonPackages folder or a subfolder of it.");
        }

        foreach (var existing in existingRepositoryPaths)
        {
            if (string.IsNullOrWhiteSpace(existing))
                continue;
            if (IsSameOrNested(candidate, Normalize(existing)))
            {
                return Result.Failure(
                    "repo.path.overlap",
                    $"The repository path overlaps an existing repository at '{existing}'.");
            }
        }

        return Result.Success();
    }

    private static bool IsSameOrNested(string a, string b)
    {
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
            return true;
        // Append a separator only when absent, so a root ("E:\") doesn't become "E:\\".
        var aSep = a.EndsWith(Path.DirectorySeparatorChar) ? a : a + Path.DirectorySeparatorChar;
        var bSep = b.EndsWith(Path.DirectorySeparatorChar) ? b : b + Path.DirectorySeparatorChar;
        return aSep.StartsWith(bSep, StringComparison.OrdinalIgnoreCase)
            || bSep.StartsWith(aSep, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
