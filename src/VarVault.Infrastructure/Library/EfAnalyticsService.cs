using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>EF analytics: space-by-creator/type over the read model, space-by-tier over VarFile→Repo. (5.16.)</summary>
public sealed class EfAnalyticsService(VarVaultDbContext db) : IAnalyticsService
{
    public async Task<IReadOnlyList<SpaceByGroup>> SpaceByCreatorAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.PackageListItems.AsNoTracking()
            .GroupBy(x => x.Creator)
            .Select(g => new SpaceByGroup(g.Key, g.Sum(x => x.TotalSize), g.Count()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.OrderByDescending(r => r.TotalBytes).ToList();
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
