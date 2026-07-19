using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Analyzer;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// EF implementation of <see cref="IUsageAnalyzer"/>: appends usage events and recomputes each
/// package's <see cref="UsageStat"/> (windowed counts + score + class via hysteresis) plus the
/// read-model class. (Checklist 5.1, BE-A1/A6.)
/// </summary>
public sealed class EfUsageAnalyzer(VarVaultDbContext db, IClock clock) : IUsageAnalyzer
{
    public async Task RecordAsync(long packageId, UsageKind kind, CancellationToken cancellationToken = default)
    {
        db.UsageEvents.Add(new UsageEvent
        {
            PackageId = packageId,
            TimestampUnixMs = clock.UtcNow.ToUnixTimeMilliseconds(),
            Kind = kind,
            Source = UsageSource.AppObserved,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RecomputeAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var packageIds = await db.UsageEvents
            .Select(e => e.PackageId)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var packageId in packageIds)
        {
            var events = await db.UsageEvents
                .Where(e => e.PackageId == packageId)
                .Select(e => e.TimestampUnixMs)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var windows = WindowedUsage.Compute(events, now);

            var centrality = await db.Packages
                .Where(p => p.Id == packageId)
                .Select(p => p.ReverseDependentCount)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            var stat = await db.UsageStats.FirstOrDefaultAsync(s => s.PackageId == packageId, cancellationToken).ConfigureAwait(false);
            if (stat is null)
            {
                stat = new UsageStat { PackageId = packageId, Class = ContentClass.Cold };
                db.UsageStats.Add(stat);
            }

            var inputs = new UsageInputs(windows.LastUsedAt, windows.Use30d, centrality, stat.IsPinnedHot, stat.IsForcedCold);
            var result = UsageScoring.Score(inputs, stat.Class, stat.LastFlipAt, now);

            stat.LastUsedAt = windows.LastUsedAt;
            // Total includes counts already compacted away (5.3) plus the live events.
            stat.UseCountTotal = stat.RolledUpUseCount + windows.UseCountTotal;
            stat.Use30d = windows.Use30d;
            stat.Use90d = windows.Use90d;
            stat.CentralityScore = centrality;
            stat.Score = result.Score;
            stat.Class = result.Class;
            stat.LastFlipAt = result.LastFlipAt;
            stat.ComputedAt = now.UtcDateTime;

            var item = await db.PackageListItems.FirstOrDefaultAsync(x => x.PackageId == packageId, cancellationToken).ConfigureAwait(false);
            if (item is not null)
            {
                item.Class = result.Class;
                item.LastUsedAt = windows.LastUsedAt;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return packageIds.Count;
    }

    public async Task<int> CompactAsync(int olderThanDays = 90, CancellationToken cancellationToken = default)
    {
        var cutoff = clock.UtcNow.AddDays(-olderThanDays).ToUnixTimeMilliseconds();

        // Count old events per package, add to the rollup, then delete them.
        var oldCounts = await db.UsageEvents
            .Where(e => e.TimestampUnixMs < cutoff)
            .GroupBy(e => e.PackageId)
            .Select(g => new { PackageId = g.Key, Count = g.LongCount() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (oldCounts.Count == 0)
            return 0;

        // Atomic: persist the rollups and delete the raw events together, so a count is never lost or doubled.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var group in oldCounts)
        {
            var stat = await db.UsageStats.FirstOrDefaultAsync(s => s.PackageId == group.PackageId, cancellationToken).ConfigureAwait(false);
            if (stat is null)
            {
                stat = new UsageStat { PackageId = group.PackageId, Class = ContentClass.Cold };
                db.UsageStats.Add(stat);
            }
            stat.RolledUpUseCount += group.Count;
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var compacted = await db.UsageEvents
            .Where(e => e.TimestampUnixMs < cutoff)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return compacted;
    }
}
