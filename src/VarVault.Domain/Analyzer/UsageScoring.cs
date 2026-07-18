using VarVault.Common;
using VarVault.Domain.Entities;

namespace VarVault.Domain.Analyzer;

/// <summary>
/// Tunable weights + thresholds for hot/warm/cold scoring. Defaults from Decisions-Log D4
/// (30d/90d windows; a flip needs a 15% deadband and ≥7 days since the last flip).
/// </summary>
public sealed record ScoringConfig(
    double RecencyWeight = 0.40,
    double FrequencyWeight = 0.35,
    double CentralityWeight = 0.25,
    double HotThreshold = 0.60,
    double WarmThreshold = 0.30,
    double FlipDeadband = 0.15,
    int FlipCooldownDays = 7,
    double RecencyHorizonDays = 90,
    double FrequencySaturation = 10,
    double CentralitySaturation = 25)
{
    public static readonly ScoringConfig Default = new();
}

/// <summary>The inputs a package's class is computed from (all persisted → reproducible). (BE-A8.)</summary>
public sealed record UsageInputs(
    DateTime? LastUsedAt,
    int Use30d,
    int ReverseDependentCount,
    bool IsPinnedHot,
    bool IsForcedCold);

/// <summary>Scoring outcome; <see cref="LastFlipAt"/> carries forward for the next hysteresis check.</summary>
public sealed record ScoringResult(double Score, ContentClass Class, bool Flipped, DateTime LastFlipAt);

/// <summary>
/// The hot/warm/cold classifier: a documented weighted blend of recency + frequency + centrality (with
/// pin/force overrides), then a hysteresis band so <c>Class</c> only flips past a 15% deadband and after
/// a 7-day cooldown. Pure and reproducible from stored inputs. (BE-A3/A4/A5/A8, 5.4.)
/// </summary>
public static class UsageScoring
{
    public static ScoringResult Score(
        UsageInputs inputs,
        ContentClass previousClass,
        DateTime? lastFlipAt,
        DateTimeOffset now,
        ScoringConfig? config = null)
    {
        Guard.NotNull(inputs);
        var c = config ?? ScoringConfig.Default;
        var nowUtc = now.UtcDateTime;

        // Explicit overrides win outright.
        if (inputs.IsPinnedHot)
            return Settle(1.0, ContentClass.Hot, previousClass, lastFlipAt, nowUtc);
        if (inputs.IsForcedCold)
            return Settle(0.0, ContentClass.Cold, previousClass, lastFlipAt, nowUtc);

        var recency = inputs.LastUsedAt is { } used
            ? Math.Clamp(1 - ((nowUtc - used).TotalDays / c.RecencyHorizonDays), 0, 1)
            : 0;
        var frequency = Math.Clamp(inputs.Use30d / c.FrequencySaturation, 0, 1);
        var centrality = Math.Clamp(inputs.ReverseDependentCount / c.CentralitySaturation, 0, 1);

        var score = (c.RecencyWeight * recency)
                  + (c.FrequencyWeight * frequency)
                  + (c.CentralityWeight * centrality);

        var candidate = ClassifyWithHysteresis(score, previousClass, c);
        return Settle(score, candidate, previousClass, lastFlipAt, nowUtc, c.FlipCooldownDays);
    }

    // Hysteresis: it's harder to enter a hotter class than to stay in it (deadband around each boundary).
    private static ContentClass ClassifyWithHysteresis(double score, ContentClass previous, ScoringConfig c)
    {
        var m = c.FlipDeadband;
        double hotEnter = c.HotThreshold + m, hotLeave = c.HotThreshold - m;
        double warmEnter = c.WarmThreshold + m, warmLeave = c.WarmThreshold - m;

        return previous switch
        {
            ContentClass.Hot => score >= hotLeave ? ContentClass.Hot
                              : score >= warmLeave ? ContentClass.Warm
                              : ContentClass.Cold,
            ContentClass.Warm => score >= hotEnter ? ContentClass.Hot
                              : score >= warmLeave ? ContentClass.Warm
                              : ContentClass.Cold,
            _ => score >= hotEnter ? ContentClass.Hot
               : score >= warmEnter ? ContentClass.Warm
               : ContentClass.Cold,
        };
    }

    private static ScoringResult Settle(
        double score, ContentClass candidate, ContentClass previous, DateTime? lastFlipAt, DateTime nowUtc, int cooldownDays = 0)
    {
        if (candidate == previous)
            return new ScoringResult(score, previous, false, lastFlipAt ?? nowUtc);

        // A flip also needs the cooldown to have elapsed (overrides use cooldownDays=0 → immediate).
        var cooldownOk = lastFlipAt is null || (nowUtc - lastFlipAt.Value).TotalDays >= cooldownDays;
        return cooldownOk
            ? new ScoringResult(score, candidate, true, nowUtc)
            : new ScoringResult(score, previous, false, lastFlipAt ?? nowUtc);
    }
}
