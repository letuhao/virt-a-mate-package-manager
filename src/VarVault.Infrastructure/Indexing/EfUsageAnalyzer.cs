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
            stat.UseCountTotal = windows.UseCountTotal;
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
}
