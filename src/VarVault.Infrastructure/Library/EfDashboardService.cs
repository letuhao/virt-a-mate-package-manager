using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N1 · Dashboard read facade. Aggregates the materialized read model (totals, classification, missing
/// deps) and the repositories (count, offline, per-tier storage). (16-checklist BE-N1.)
/// </summary>
public sealed class EfDashboardService(VarVaultDbContext db, IRepositoryService repositories) : IDashboardService
{
    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var totalPackages = await db.PackageListItems.CountAsync(cancellationToken).ConfigureAwait(false);
        var totalBytes = totalPackages == 0
            ? 0L
            : await db.PackageListItems.SumAsync(x => x.TotalSize, cancellationToken).ConfigureAwait(false);
        var activeInGame = await db.PackageListItems.CountAsync(x => x.IsActive, cancellationToken).ConfigureAwait(false);
        var missing = await db.PackageListItems.CountAsync(x => x.HasMissingDeps, cancellationToken).ConfigureAwait(false);

        var classCounts = await db.PackageListItems
            .GroupBy(x => x.Class)
            .Select(g => new { Class = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Class, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        var repos = await repositories.ListAsync(cancellationToken).ConfigureAwait(false);
        var tiers = repos
            .GroupBy(r => r.Tier)
            .Select(g => new TierUtilization(
                Tier: g.Key,
                UsedBytes: g.Sum(r => r.CapacityBytes is { } c && r.FreeBytes is { } f ? Math.Max(0, c - f) : 0),
                CapacityBytes: g.Sum(r => r.CapacityBytes ?? 0),
                RepositoryCount: g.Count()))
            .OrderBy(t => t.Tier)
            .ToList();

        return new DashboardSummary(
            TotalPackages: totalPackages,
            TotalBytes: totalBytes,
            RepositoryCount: repos.Count,
            OfflineRepositoryCount: repos.Count(r => !r.IsOnline),
            ActiveInGame: activeInGame,
            HotCount: classCounts.GetValueOrDefault(ContentClass.Hot),
            WarmCount: classCounts.GetValueOrDefault(ContentClass.Warm),
            ColdCount: classCounts.GetValueOrDefault(ContentClass.Cold),
            MissingDepsCount: missing,
            Tiers: tiers);
    }
}
