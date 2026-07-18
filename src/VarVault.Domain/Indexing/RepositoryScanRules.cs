using VarVault.Domain.Entities;

namespace VarVault.Domain.Indexing;

/// <summary>
/// Pure rules for how a repository directory tree is scanned: which directories are the legacy
/// quarantine buckets, which are the app's own symlink farms (never indexed as vars), and freshness
/// comparison. Matched by <b>prefix</b> because the old varManager appends a suffix to quarantine
/// dirs (e.g. <c>___VarRedundant____notSameContentFiles1</c>). (IDX-1/2/11, checklist 1.11/1.12/1.29.)
/// </summary>
public static class RepositoryScanRules
{
    // Legacy quarantine directory prefixes → the kind they represent.
    private static readonly (string Prefix, QuarantineKind Kind)[] QuarantinePrefixes =
    [
        ("___VarRedundant___", QuarantineKind.Redundant),
        ("___StaleVars___", QuarantineKind.Stale),
        ("___OldVersionVars___", QuarantineKind.OldVersion),
        ("___DeletedVars___", QuarantineKind.Deleted),
    ];

    // The app's own symlink-farm directories inside a profile — never scanned for source vars.
    private static readonly string[] LinkDirPrefixes =
    [
        "___VarsLink___", "___MissingVarLink___", "___TempVarLink___", "___AddonPacksSwitch",
    ];

    /// <summary>Quarantine kind for a directory name (by prefix), or <see cref="QuarantineKind.None"/>.</summary>
    public static QuarantineKind ClassifyDirectory(string directoryName)
    {
        foreach (var (prefix, kind) in QuarantinePrefixes)
        {
            if (directoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return kind;
        }
        return QuarantineKind.None;
    }

    /// <summary>True if a directory is one of the app's symlink farms (skip entirely during a source scan).</summary>
    public static bool IsLinkDirectory(string directoryName)
    {
        foreach (var prefix in LinkDirPrefixes)
        {
            if (directoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Freshness decision from directory-entry facts alone (no file open): a var is fresh (skip)
    /// when size is unchanged and mtime matches within <paramref name="mtimeToleranceSeconds"/>
    /// (2 s for FAT/exFAT/network per decision D5; 0 for fixed NTFS).
    /// </summary>
    public static bool IsFresh(
        long storedSizeBytes,
        DateTime storedMtimeUtc,
        long currentSizeBytes,
        DateTime currentMtimeUtc,
        double mtimeToleranceSeconds = 0)
    {
        if (storedSizeBytes != currentSizeBytes)
            return false;
        var delta = Math.Abs((currentMtimeUtc - storedMtimeUtc).TotalSeconds);
        return delta <= mtimeToleranceSeconds;
    }
}
