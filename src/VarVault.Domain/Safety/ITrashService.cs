using VarVault.Common;

namespace VarVault.Domain.Safety;

/// <summary>A trashed file, described by its in-trash manifest (survives DB loss).</summary>
public sealed record TrashEntry(
    string Id,
    string OriginalPath,
    string TrashPath,
    string Reason,
    DateTime TrashedAtUtc,
    long Bytes);

/// <summary>
/// ⚠ Safety infrastructure: every delete routes here — a file is <b>moved</b> to trash (same-volume
/// where possible), never hard-deleted, and a per-item manifest is written inside the trash so a
/// restore works even if the catalog DB is lost. (Data-arch §5.9; checklist X.1/X.2/X.3.)
/// </summary>
public interface ITrashService
{
    /// <summary>Move <paramref name="sourcePath"/> into trash and write its manifest.</summary>
    Task<Result<TrashEntry>> TrashAsync(string sourcePath, string reason, CancellationToken cancellationToken = default);

    /// <summary>Restore a trashed item to its original path (fails if something is already there).</summary>
    Task<Result> RestoreAsync(string trashId, CancellationToken cancellationToken = default);

    /// <summary>Restore directly from a manifest on disk — no DB required. (X.2)</summary>
    Task<Result> RestoreFromManifestAsync(string manifestPath, CancellationToken cancellationToken = default);

    /// <summary>Enumerate trashed items by reading manifests (DB-independent).</summary>
    Task<IReadOnlyList<TrashEntry>> ListAsync(CancellationToken cancellationToken = default);
}
