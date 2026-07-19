using VarVault.Common;

namespace VarVault.Sdk.Library;

/// <summary>A trashed item for the Trash screen.</summary>
public sealed record TrashItemDto(string Id, string OriginalPath, string Reason, DateTime TrashedAtUtc, long Bytes);

/// <summary>A catalog backup on disk.</summary>
public sealed record BackupDto(string Path, DateTime CreatedAtUtc, long Bytes);

/// <summary>
/// BE-N6 · Trash & backup facade over ITrashService + SqliteDatabaseBackup: list/restore/purge trashed
/// items and list/create catalog backups. (16-checklist BE-N6.)
/// </summary>
public interface ITrashQueryService
{
    Task<IReadOnlyList<TrashItemDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result> RestoreAsync(string trashId, CancellationToken cancellationToken = default);
    Task<Result> PurgeAsync(string trashId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupDto>> ListBackupsAsync(CancellationToken cancellationToken = default);
    Task<Result<BackupDto>> BackupNowAsync(CancellationToken cancellationToken = default);
}
