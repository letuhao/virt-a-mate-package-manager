using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Safety;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N6 · Trash &amp; backup facade. Delegates to <see cref="ITrashService"/> for list/restore/purge and
/// <see cref="SqliteDatabaseBackup"/> for backups (dir derived from the catalog DB path). (16-checklist BE-N6.)
/// </summary>
public sealed class EfTrashQueryService(VarVaultDbContext db, ITrashService trash, SqliteDatabaseBackup backup) : ITrashQueryService
{
    private string BackupDir =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(db.Database.GetDbConnection().DataSource)) ?? ".", "backups");

    public async Task<PageResult<TrashItemDto>> ListPageAsync(
        PageRequest request,
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        var page = request.Normalize();
        var entries = await trash.ListAsync(cancellationToken).ConfigureAwait(false);
        var ordered = entries
            .Where(e => string.IsNullOrWhiteSpace(searchText)
                || e.OriginalPath.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || e.Reason.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.TrashedAtUtc)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

        var items = ordered
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(e => new TrashItemDto(e.Id, e.OriginalPath, e.Reason, e.TrashedAtUtc, e.Bytes))
            .ToList();
        return new PageResult<TrashItemDto>(items, ordered.Count, page.SafePageNumber, page.SafePageSize);
    }

    public async Task<IReadOnlyList<TrashItemDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        return (await ListPageAsync(new PageRequest(1, 100), cancellationToken: cancellationToken).ConfigureAwait(false)).Items;
    }

    public Task<Result> RestoreAsync(string trashId, CancellationToken cancellationToken = default) =>
        trash.RestoreAsync(trashId, cancellationToken);

    public Task<Result> PurgeAsync(string trashId, CancellationToken cancellationToken = default) =>
        trash.PurgeAsync(trashId, cancellationToken);

    public Task<IReadOnlyList<BackupDto>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BackupDto> list = backup.ListBackups(BackupDir)
            .Select(b => new BackupDto(b.Path, b.CreatedAtUtc, b.Bytes))
            .ToList();
        return Task.FromResult(list);
    }

    public async Task<Result<BackupDto>> BackupNowAsync(CancellationToken cancellationToken = default)
    {
        var result = await backup.BackupAsync(BackupDir, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Result.Success(new BackupDto(result.Value.Path, result.Value.CreatedAtUtc, result.Value.Bytes))
            : Result.Failure<BackupDto>(result.Error.Code, result.Error.Message);
    }
}
