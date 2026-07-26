using VarVault.Common;

namespace VarVault.Domain.Analyzer;

/// <summary>Time-relative usage windows recomputed against "now" (correct even with no new events).</summary>
public sealed record UsageWindows(DateTime? LastUsedAt, long UseCountTotal, int Use30d, int Use90d);

/// <summary>
/// Computes windowed usage counts from raw event timestamps, always relative to the current
/// time — so a package silently cools as the clock advances, with no new events required. (BE-A2, 5.2.)
/// Property names Use30d/Use90d are historical; windows are parameterized.
/// </summary>
public static class WindowedUsage
{
    private const long DayMs = 86_400_000L;

    public static UsageWindows Compute(
        IReadOnlyList<long> eventTimestampsUnixMs,
        DateTimeOffset now,
        int primaryWindowDays = 30,
        int secondaryWindowDays = 90)
    {
        Guard.NotNull(eventTimestampsUnixMs);
        primaryWindowDays = Math.Clamp(primaryWindowDays, 1, 365);
        secondaryWindowDays = Math.Clamp(Math.Max(secondaryWindowDays, primaryWindowDays), 1, 365);

        var nowMs = now.ToUnixTimeMilliseconds();
        var cutPrimary = nowMs - (primaryWindowDays * DayMs);
        var cutSecondary = nowMs - (secondaryWindowDays * DayMs);

        int uPrimary = 0, uSecondary = 0;
        long? last = null;
        foreach (var t in eventTimestampsUnixMs)
        {
            if (t >= cutPrimary) uPrimary++;
            if (t >= cutSecondary) uSecondary++;
            if (last is null || t > last)
                last = t;
        }

        var lastUsed = last is { } l ? DateTimeOffset.FromUnixTimeMilliseconds(l).UtcDateTime : (DateTime?)null;
        return new UsageWindows(lastUsed, eventTimestampsUnixMs.Count, uPrimary, uSecondary);
    }
}
