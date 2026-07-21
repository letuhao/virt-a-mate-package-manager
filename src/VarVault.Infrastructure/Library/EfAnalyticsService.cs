using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;

namespace VarVault.Infrastructure.Library;

/// <summary>EF analytics: space-by-creator/type over the read model, space-by-tier over VarFile→Repo. (5.16.)</summary>
public sealed class EfAnalyticsService(VarVaultDbContext db) : IAnalyticsService
{
    public async Task<PageResult<SpaceByGroup>> SpaceByCreatorPageAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        var page = request.Normalize();
        // Project to anonymous type first — EF cannot translate `new SpaceByGroup(...)` inside GroupBy.
        var query = db.PackageListItems.AsNoTracking()
            .GroupBy(x => x.Creator)
            .Select(g => new { Group = g.Key, TotalBytes = g.Sum(x => x.TotalSize), Count = g.Count() });
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(x => x.TotalBytes)
            .ThenBy(x => x.Group)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Select(x => new SpaceByGroup(x.Group, x.TotalBytes, x.Count)).ToList();
        return new PageResult<SpaceByGroup>(items, total, page.SafePageNumber, page.SafePageSize);
    }

    public async Task<IReadOnlyList<SpaceByGroup>> SpaceByCreatorAsync(CancellationToken cancellationToken = default)
    {
        return (await SpaceByCreatorPageAsync(new PageRequest(1, 100), cancellationToken).ConfigureAwait(false)).Items;
    }

    public async Task<IReadOnlyList<SpaceByGroup>> SpaceByTypeAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.PackageListItems.AsNoTracking()
            .GroupBy(x => x.PrimaryType)
            .Select(g => new { Type = g.Key, Bytes = g.Sum(x => x.TotalSize), Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows
            .Select(r => new SpaceByGroup(r.Type.ToString(), r.Bytes, r.Count))
            .OrderByDescending(r => r.TotalBytes).ToList();
    }

    public async Task<IReadOnlyList<SpaceByGroup>> SpaceByTierAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.VarFiles.AsNoTracking()
            .Join(db.Repositories, v => v.RepositoryId, r => r.Id, (v, r) => new { r.Tier, v.SizeBytes })
            .GroupBy(x => x.Tier)
            .Select(g => new { Tier = g.Key, Bytes = g.Sum(x => x.SizeBytes), Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows
            .Select(r => new SpaceByGroup($"Tier {r.Tier.ToString(CultureInfo.InvariantCulture)}", r.Bytes, r.Count))
            .OrderBy(r => r.Group, StringComparer.Ordinal).ToList();
    }
}
