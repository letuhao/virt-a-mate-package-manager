using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// EF read-model query over <see cref="PackageListItem"/>: applies filters, an index-backed sort, and
/// paging, projecting to SDK DTOs. Read-only (`AsNoTracking`). (Checklist 1.38/1.41/1.46.)
/// </summary>
public sealed class EfLibraryQueryService(VarVaultDbContext db) : ILibraryQueryService
{
    public async Task<LibraryPage> GetPageAsync(LibraryQuery query, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(query);

        var q = await BuildFilteredAsync(query, cancellationToken).ConfigureAwait(false);
        var total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

        q = ApplySort(q, query.Sort, query.Descending);

        var rows = await q
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Clamp(query.Take, 1, 1000))
            .Select(x => new PackageListEntry(
                x.PackageId, x.VarName, x.Creator, x.PackageName, x.VersionToken,
                x.PrimaryType.ToString(), x.TotalSize, x.OnlineInstanceCount, x.TotalInstanceCount,
                x.IsSingleCopy, x.IsFavorite, x.Class.ToString(), x.HasMissingDeps, x.LastUsedAt, x.ActualTierMin))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new LibraryPage(rows, total);
    }

    public async Task<IReadOnlyList<long>> GetOrderedIdsAsync(LibraryQuery query, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(query);
        var q = await BuildFilteredAsync(query, cancellationToken).ConfigureAwait(false);
        return await ApplySort(q, query.Sort, query.Descending)
            .Select(x => x.PackageId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IQueryable<PackageListItem>> BuildFilteredAsync(LibraryQuery query, CancellationToken cancellationToken)
    {
        var q = db.PackageListItems.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Creator))
            q = q.Where(x => x.Creator == query.Creator);
        if (query.FavoritesOnly)
            q = q.Where(x => x.IsFavorite);
        if (query.MissingDepsOnly)
            q = q.Where(x => x.HasMissingDeps);
        if (query.InstalledOnly)
            q = q.Where(x => x.IsActive);
        if (query.SingleCopyOnly)
            q = q.Where(x => x.IsSingleCopy);
        if (!string.IsNullOrWhiteSpace(query.PackageName))
            q = q.Where(x => EF.Functions.Like(x.PackageName, $"%{query.PackageName}%"));
        if (query.Types is { Count: > 0 })
        {
            var typeEnums = query.Types
                .Select(t => Enum.TryParse<Domain.Entities.ContentType>(t, out var e) ? e : (Domain.Entities.ContentType?)null)
                .Where(e => e is not null).Select(e => e!.Value).ToList();
            if (typeEnums.Count > 0)
                q = q.Where(x => typeEnums.Contains(x.PrimaryType));
        }
        if (query.Tiers is { Count: > 0 })
            q = q.Where(x => x.ActualTierMin != null && query.Tiers.Contains(x.ActualTierMin.Value));
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var text = query.SearchText.Trim();
            // Trigram FTS needs ≥3 chars (and handles space-less CJK); shorter → substring LIKE.
            if (text.Length >= 3)
            {
                var matchIds = await SearchIdsAsync(text, cancellationToken).ConfigureAwait(false);
                q = q.Where(x => matchIds.Contains(x.PackageId));
            }
            else
            {
                q = q.Where(x => EF.Functions.Like(x.VarName, $"%{text}%"));
            }
        }

        return q;
    }

    public async Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken cancellationToken = default) =>
        await db.PackageListItems.AsNoTracking()
            .Select(x => x.Creator).Distinct().OrderBy(c => c)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<CreatorCount>> GetCreatorCountsAsync(CancellationToken cancellationToken = default) =>
        await db.PackageListItems.AsNoTracking()
            .GroupBy(x => x.Creator)
            .OrderBy(g => g.Key)
            .Select(g => new CreatorCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    // FTS5 trigram MATCH → matching package ids (rowid = PackageId). Handles CJK. (1.40)
    private async Task<HashSet<long>> SearchIdsAsync(string text, CancellationToken cancellationToken)
    {
        var quoted = "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        var ids = await db.Database
            .SqlQueryRaw<long>("SELECT rowid AS \"Value\" FROM PackageSearch WHERE Blob MATCH {0}", quoted)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.ToHashSet();
    }

    // Each order maps to a composite index from data-arch §6 (trailing PackageId keeps it stable).
    private static IQueryable<PackageListItem> ApplySort(IQueryable<PackageListItem> q, LibrarySort sort, bool desc) => sort switch
    {
        LibrarySort.Creator => desc
            ? q.OrderByDescending(x => x.Creator).ThenByDescending(x => x.TotalSize).ThenByDescending(x => x.PackageId)
            : q.OrderBy(x => x.Creator).ThenBy(x => x.TotalSize).ThenBy(x => x.PackageId),
        LibrarySort.Size => desc
            ? q.OrderByDescending(x => x.TotalSize).ThenBy(x => x.PackageId)
            : q.OrderBy(x => x.TotalSize).ThenBy(x => x.PackageId),
        LibrarySort.LastUsed => desc
            ? q.OrderByDescending(x => x.LastUsedAt).ThenBy(x => x.PackageId)
            : q.OrderBy(x => x.LastUsedAt).ThenBy(x => x.PackageId),
        LibrarySort.Class => desc
            ? q.OrderByDescending(x => x.Class).ThenByDescending(x => x.LastUsedAt).ThenBy(x => x.PackageId)
            : q.OrderBy(x => x.Class).ThenBy(x => x.LastUsedAt).ThenBy(x => x.PackageId),
        _ => desc
            ? q.OrderByDescending(x => x.VarName).ThenBy(x => x.PackageId)
            : q.OrderBy(x => x.VarName).ThenBy(x => x.PackageId),
    };
}
