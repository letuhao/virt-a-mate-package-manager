using VarVault.Common;
using VarVault.Sdk.Paging;

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
    async Task<PageResult<TrashItemDto>> ListPageAsync(
        PageRequest request,
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        var all = await ListAsync(cancellationToken).ConfigureAwait(false);
        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? all
            : all.Where(x => x.OriginalPath.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || x.Reason.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
        var page = request.Normalize();
        return new PageResult<TrashItemDto>(
            filtered.Skip(page.Skip).Take(page.SafePageSize).ToList(),
            filtered.Count,
            page.SafePageNumber,
            page.SafePageSize);
    }

    Task<IReadOnlyList<TrashItemDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result> RestoreAsync(string trashId, CancellationToken cancellationToken = default);
    Task<Result> PurgeAsync(string trashId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupDto>> ListBackupsAsync(CancellationToken cancellationToken = default);
    Task<Result<BackupDto>> BackupNowAsync(CancellationToken cancellationToken = default);
}
