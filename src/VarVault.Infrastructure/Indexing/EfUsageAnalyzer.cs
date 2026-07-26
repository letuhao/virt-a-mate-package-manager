using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Analyzer;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Settings;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// EF implementation of <see cref="IUsageAnalyzer"/>: appends usage events and recomputes each
/// package's <see cref="UsageStat"/> (windowed counts + score + class via hysteresis) plus the
/// read-model class. Catalog mutations go through <see cref="IWriteQueue"/>. (Checklist 5.1, BE-A1/A6.)
/// </summary>
public sealed class EfUsageAnalyzer(
    VarVaultDbContext db,
    IClock clock,
    IWriteQueue? writeQueue = null,
    ISettingsService? settings = null) : IUsageAnalyzer
{
    public Task RecordAsync(long packageId, UsageKind kind, CancellationToken cancellationToken = default) =>
        RecordManyAsync([packageId], kind, UsageSource.AppObserved, null, cancellationToken);

    public Task RecordManyAsync(
        IReadOnlyList<long> packageIds,
        UsageKind kind,
        CancellationToken cancellationToken = default) =>
        RecordManyAsync(packageIds, kind, UsageSource.AppObserved, null, cancellationToken);

    public Task RecordManyAsync(
        IReadOnlyList<long> packageIds,
        UsageKind kind,
        UsageSource source,
        DateTimeOffset? timestampUtc,
        CancellationToken cancellationToken = default) =>
        WriteAsync(ct => RecordManyCoreAsync(packageIds, kind, source, timestampUtc, ct),
            WritePriority.Interactive, cancellationToken);

    public Task SetPlacementOverridesAsync(
        long packageId,
        bool pinHot,
        bool forceCold,
        CancellationToken cancellationToken = default) =>
        WriteAsync(ct => SetPlacementOverridesCoreAsync(packageId, pinHot, forceCold, ct),
            WritePriority.Interactive, cancellationToken);

    public Task<int> RecomputeAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(async ct =>
        {
            var packageIds = await db.UsageEvents
                .Select(e => e.PackageId)
                .Distinct()
                .ToListAsync(ct).ConfigureAwait(false);
            var overrideOnly = await db.UsageStats
                .Where(s => s.IsPinnedHot || s.IsForcedCold)
                .Select(s => s.PackageId)
                .ToListAsync(ct).ConfigureAwait(false);
            return await RecomputePackagesCoreAsync(packageIds.Concat(overrideOnly).Distinct().ToList(), ct)
                .ConfigureAwait(false);
        }, WritePriority.Normal, cancellationToken);

    public Task<int> RecomputePackagesAsync(
        IReadOnlyList<long> packageIds,
        CancellationToken cancellationToken = default) =>
        WriteAsync(ct => RecomputePackagesCoreAsync(packageIds, ct), WritePriority.Normal, cancellationToken);

    public Task<int> CompactAsync(int olderThanDays = 90, CancellationToken cancellationToken = default) =>
        WriteAsync(ct => CompactCoreAsync(olderThanDays, ct), WritePriority.Bulk, cancellationToken);

    private Task WriteAsync(Func<CancellationToken, Task> write, WritePriority priority, CancellationToken cancellationToken) =>
        writeQueue is null
            ? write(cancellationToken)
            : writeQueue.EnqueueAsync(write, priority, cancellationToken);

    private Task<T> WriteAsync<T>(Func<CancellationToken, Task<T>> write, WritePriority priority, CancellationToken cancellationToken) =>
        writeQueue is null
            ? write(cancellationToken)
            : writeQueue.EnqueueAsync(write, priority, cancellationToken);

    private async Task RecordManyCoreAsync(
        IReadOnlyList<long> packageIds,
        UsageKind kind,
        UsageSource source,
        DateTimeOffset? timestampUtc,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(packageIds);
        if (packageIds.Count == 0)
            return;

        var nowMs = (timestampUtc ?? clock.UtcNow).ToUnixTimeMilliseconds();
        var seen = new HashSet<long>();
        foreach (var packageId in packageIds)
        {
            if (packageId <= 0 || !seen.Add(packageId))
                continue;
            db.UsageEvents.Add(new UsageEvent
            {
                PackageId = packageId,
                TimestampUnixMs = nowMs,
                Kind = kind,
                Source = source,
            });
        }

        if (seen.Count == 0)
            return;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SetPlacementOverridesCoreAsync(
        long packageId,
        bool pinHot,
        bool forceCold,
        CancellationToken cancellationToken)
    {
        if (packageId <= 0)
            throw new ArgumentOutOfRangeException(nameof(packageId));
        if (pinHot && forceCold)
            forceCold = false;

        var stat = await db.UsageStats.FirstOrDefaultAsync(s => s.PackageId == packageId, cancellationToken)
            .ConfigureAwait(false);
        if (stat is null)
        {
            stat = new UsageStat { PackageId = packageId, Class = ContentClass.Cold };
            db.UsageStats.Add(stat);
        }

        stat.IsPinnedHot = pinHot;
        stat.IsForcedCold = forceCold;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RecomputePackagesCoreAsync([packageId], cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> RecomputePackagesCoreAsync(
        IReadOnlyList<long> packageIds,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(packageIds);
        if (packageIds.Count == 0)
            return 0;

        var now = clock.UtcNow;
        var config = await LoadScoringConfigAsync(cancellationToken).ConfigureAwait(false);
        var primaryDays = (int)Math.Clamp(config.RecencyHorizonDays, 1, 365);
        var secondaryDays = Math.Min(primaryDays * 3, 365);
        var distinct = packageIds.Where(id => id > 0).Distinct().ToList();
        foreach (var packageId in distinct)
        {
            var events = await db.UsageEvents
                .Where(e => e.PackageId == packageId)
                .Select(e => e.TimestampUnixMs)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var windows = WindowedUsage.Compute(events, now, primaryDays, secondaryDays);

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
            var result = UsageScoring.Score(inputs, stat.Class, stat.LastFlipAt, now, config);

            stat.LastUsedAt = windows.LastUsedAt;
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
        return distinct.Count;
    }

    private async Task<int> CompactCoreAsync(int olderThanDays, CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow.AddDays(-olderThanDays).ToUnixTimeMilliseconds();

        var oldCounts = await db.UsageEvents
            .Where(e => e.TimestampUnixMs < cutoff)
            .GroupBy(e => e.PackageId)
            .Select(g => new { PackageId = g.Key, Count = g.LongCount() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (oldCounts.Count == 0)
            return 0;

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

    private async Task<ScoringConfig> LoadScoringConfigAsync(CancellationToken cancellationToken)
    {
        if (settings is null)
            return ScoringConfig.Default;
        var raw = await settings.GetAsync(SettingKeys.HotThresholdDays, cancellationToken).ConfigureAwait(false);
        if (!int.TryParse(raw, out var days) || days <= 0)
            days = 30;
        days = Math.Clamp(days, 1, 365);
        return ScoringConfig.Default with { RecencyHorizonDays = days };
    }
}
