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

        var rows = await ProjectAsync(q
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Clamp(query.Take, 1, 1000)), cancellationToken).ConfigureAwait(false);

        return new LibraryPage(rows, total);
    }

    public async Task<IReadOnlyList<PackageListEntry>> GetByIdsAsync(
        IReadOnlyList<long> packageIds,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(packageIds);
        if (packageIds.Count == 0)
            return [];
        var ids = packageIds.Distinct().Take(1000).ToList();
        var rows = await ProjectAsync(
            db.PackageListItems.AsNoTracking().Where(x => ids.Contains(x.PackageId)),
            cancellationToken).ConfigureAwait(false);
        var byId = rows.ToDictionary(x => x.PackageId);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    private async Task<IReadOnlyList<PackageListEntry>> ProjectAsync(
        IQueryable<PackageListItem> query,
        CancellationToken cancellationToken)
    {
        var page = await query
            .Select(x => new
            {
                x.PackageId, x.VarName, x.Creator, x.PackageName, x.VersionToken,
                x.PrimaryType, x.TotalSize, x.OnlineInstanceCount, x.TotalInstanceCount,
                x.IsSingleCopy, x.IsFavorite, x.Class, x.HasMissingDeps, x.LastUsedAt, x.ActualTierMin, x.AddedAt, x.InstalledAt, x.IsActive,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Per-content-type counts for the page's packages — already materialized in PackageContentCount. (24-checklist E3)
        var ids = page.Select(p => p.PackageId).ToList();
        var countsByPkg = (await db.PackageContentCounts.AsNoTracking()
                .Where(c => ids.Contains(c.PackageId))
                .Select(c => new { c.PackageId, c.Type, c.Count })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(c => c.PackageId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, int>)g.ToDictionary(c => c.Type.ToString(), c => c.Count));

        // Forward-dependency count per package (via its canonical var file) — secondary query, no migration. (doc 26 · G-2.3)
        var canonical = await db.Packages.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.CanonicalVarFileId != null)
            .Select(p => new { p.Id, VarId = p.CanonicalVarFileId!.Value })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var canonicalIds = canonical.Select(c => c.VarId).ToList();
        var depCountByVar = (await db.Dependencies.AsNoTracking()
                .Where(d => canonicalIds.Contains(d.VarFileId))
                .GroupBy(d => d.VarFileId)
                .Select(g => new { VarFileId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(x => x.VarFileId, x => x.Count);
        var depCountByPkg = canonical.ToDictionary(c => c.Id, c => depCountByVar.GetValueOrDefault(c.VarId));

        var rows = page.Select(x => new PackageListEntry(
                x.PackageId, x.VarName, x.Creator, x.PackageName, x.VersionToken,
                x.PrimaryType.ToString(), x.TotalSize, x.OnlineInstanceCount, x.TotalInstanceCount,
                x.IsSingleCopy, x.IsFavorite, x.Class.ToString(), x.HasMissingDeps, x.LastUsedAt, x.ActualTierMin,
                countsByPkg.GetValueOrDefault(x.PackageId),
                x.AddedAt, x.InstalledAt, depCountByPkg.GetValueOrDefault(x.PackageId), x.IsActive)).ToList();

        return rows;
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
        if (query.TagId is { } tagId)
            q = q.Where(x => db.PackageTags.Any(t => t.TagId == tagId && t.PackageId == x.PackageId));
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var text = query.SearchText.Trim();
            // Trigram FTS needs ≥3 chars (and handles space-less CJK); shorter → substring LIKE.
            // If FTS returns nothing (index empty/stale), fall back to multi-field LIKE so search
            // still works against PackageListItem rather than looking "broken".
            if (text.Length >= 3)
            {
                var matchIds = await SearchIdsAsync(text, cancellationToken).ConfigureAwait(false);
                if (matchIds.Count > 0)
                    q = q.Where(x => matchIds.Contains(x.PackageId));
                else
                    q = ApplySubstringSearch(q, text);
            }
            else
            {
                q = ApplySubstringSearch(q, text);
            }
        }

        return q;
    }

    private static IQueryable<PackageListItem> ApplySubstringSearch(IQueryable<PackageListItem> q, string text) =>
        q.Where(x => EF.Functions.Like(x.VarName, $"%{text}%")
                     || EF.Functions.Like(x.Creator, $"%{text}%")
                     || EF.Functions.Like(x.PackageName, $"%{text}%"));

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
        LibrarySort.Added => desc
            // Default library order (Quality #1): AddedAt ↓ then VarName ↑ then PackageId.
            ? q.OrderByDescending(x => x.AddedAt).ThenBy(x => x.VarName).ThenBy(x => x.PackageId)
            : q.OrderBy(x => x.AddedAt).ThenBy(x => x.VarName).ThenBy(x => x.PackageId),
        LibrarySort.Installed => desc
            ? q.OrderBy(x => x.InstalledAt == null).ThenByDescending(x => x.InstalledAt).ThenBy(x => x.VarName).ThenBy(x => x.PackageId)
            : q.OrderBy(x => x.InstalledAt == null).ThenBy(x => x.InstalledAt).ThenBy(x => x.VarName).ThenBy(x => x.PackageId),
        // Name primary still keeps AddedAt as the secondary key so equal names (shouldn't happen) / stable browse feel.
        _ => desc
            ? q.OrderByDescending(x => x.VarName).ThenByDescending(x => x.AddedAt).ThenBy(x => x.PackageId)
            : q.OrderBy(x => x.VarName).ThenByDescending(x => x.AddedAt).ThenBy(x => x.PackageId),
    };
}
