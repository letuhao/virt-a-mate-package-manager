namespace VarVault.Domain.Indexing;

/// <summary>
/// A cheap, order-independent fingerprint of a repository's <c>.var</c> directory entries: file count,
/// total bytes, newest mtime, and an XOR-folded per-file hash of <c>(relative path, size, mtime)</c>.
/// Computed from directory metadata alone (no file open). If a repository's signature is unchanged
/// since the last completed scan, nothing on disk changed and a full re-index can be skipped —
/// the startup/auto-index fast-path. (A16.)
/// </summary>
/// <remarks>
/// Sensitive to adds, deletes, renames/moves, size changes, and mtime changes — the same facts the
/// per-file freshness rule uses. It cannot see a content edit that keeps size AND mtime identical
/// (the same blind spot as <see cref="RepositoryScanRules.IsFresh"/>); a Force full re-index covers that.
/// </remarks>
public readonly record struct RepositorySignature(
    long FileCount,
    long TotalBytes,
    long NewestMtimeTicks,
    long PathsHash)
{
    /// <summary>The zero signature — the starting accumulator and the value for an empty repository.</summary>
    public static RepositorySignature Empty => default;

    /// <summary>Fold one discovered var into the running signature (immutable; returns the new value).</summary>
    public RepositorySignature Add(long sizeBytes, DateTime fileMtimeUtc, string relativePath) =>
        new(
            FileCount + 1,
            TotalBytes + sizeBytes,
            Math.Max(NewestMtimeTicks, fileMtimeUtc.Ticks),
            PathsHash ^ PerFileHash(relativePath, sizeBytes, fileMtimeUtc));

    // 64-bit FNV-1a over (path, size, mtime), XOR-folded across files so enumeration order doesn't
    // matter. Relative paths are unique within a repository, so no accidental self-cancellation.
    private static long PerFileHash(string relativePath, long sizeBytes, DateTime fileMtimeUtc)
    {
        unchecked
        {
            const ulong prime = 1099511628211UL;
            var h = 1469598103934665603UL;
            foreach (var c in relativePath)
                h = (h ^ c) * prime;
            h = (h ^ (ulong)sizeBytes) * prime;
            h = (h ^ (ulong)fileMtimeUtc.Ticks) * prime;
            return (long)h;
        }
    }
}
