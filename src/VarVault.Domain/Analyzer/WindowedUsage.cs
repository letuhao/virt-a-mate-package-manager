using VarVault.Common;

namespace VarVault.Domain.Analyzer;

/// <summary>Time-relative usage windows recomputed against "now" (correct even with no new events).</summary>
public sealed record UsageWindows(DateTime? LastUsedAt, long UseCountTotal, int Use30d, int Use90d);

/// <summary>
/// Computes windowed usage counts (30d/90d) from raw event timestamps, always relative to the current
/// time — so a package silently cools as the clock advances, with no new events required. (BE-A2, 5.2.)
/// </summary>
public static class WindowedUsage
{
    private const long DayMs = 86_400_000L;

    public static UsageWindows Compute(IReadOnlyList<long> eventTimestampsUnixMs, DateTimeOffset now)
    {
        Guard.NotNull(eventTimestampsUnixMs);

        var nowMs = now.ToUnixTimeMilliseconds();
        var cut30 = nowMs - (30 * DayMs);
        var cut90 = nowMs - (90 * DayMs);

        int u30 = 0, u90 = 0;
        long? last = null;
        foreach (var t in eventTimestampsUnixMs)
        {
            if (t >= cut30) u30++;
            if (t >= cut90) u90++;
            if (last is null || t > last)
                last = t;
        }

        var lastUsed = last is { } l ? DateTimeOffset.FromUnixTimeMilliseconds(l).UtcDateTime : (DateTime?)null;
        return new UsageWindows(lastUsed, eventTimestampsUnixMs.Count, u30, u90);
    }
}
