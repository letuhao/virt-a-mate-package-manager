using VarVault.Domain.Analyzer;
using VarVault.Domain.Entities;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class WindowedUsageTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static long DaysAgo(int d) => Now.AddDays(-d).ToUnixTimeMilliseconds();

    [Fact]
    public void Counts_events_in_30_and_90_day_windows()
    {
        var events = new[] { DaysAgo(5), DaysAgo(20), DaysAgo(45), DaysAgo(120) };
        var w = WindowedUsage.Compute(events, Now);

        Assert.Equal(2, w.Use30d);  // 5, 20
        Assert.Equal(3, w.Use90d);  // 5, 20, 45
        Assert.Equal(4, w.UseCountTotal);
    }

    [Fact]
    public void Windows_are_time_relative_as_the_clock_advances()
    {
        var events = new[] { DaysAgo(25) };
        Assert.Equal(1, WindowedUsage.Compute(events, Now).Use30d);
        // 10 days later the same event is now 35 days old → drops out of the 30d window (no new events).
        Assert.Equal(0, WindowedUsage.Compute(events, Now.AddDays(10)).Use30d);
    }

    [Fact]
    public void Last_used_is_the_most_recent_event()
    {
        var events = new[] { DaysAgo(40), DaysAgo(3), DaysAgo(90) };
        var w = WindowedUsage.Compute(events, Now);
        Assert.Equal(Now.AddDays(-3).UtcDateTime, w.LastUsedAt);
    }

    [Fact]
    public void No_events_yields_null_last_used()
    {
        var w = WindowedUsage.Compute([], Now);
        Assert.Null(w.LastUsedAt);
        Assert.Equal(0, w.Use90d);
    }

    [Fact]
    public void Custom_primary_window_changes_Use30d_counts()
    {
        var events = new[] { DaysAgo(5), DaysAgo(20), DaysAgo(45) };
        var narrow = WindowedUsage.Compute(events, Now, primaryWindowDays: 7, secondaryWindowDays: 21);
        Assert.Equal(1, narrow.Use30d); // only day-5
        Assert.Equal(2, narrow.Use90d); // 5 + 20

        var wide = WindowedUsage.Compute(events, Now, primaryWindowDays: 90, secondaryWindowDays: 270);
        Assert.Equal(3, wide.Use30d);
    }
}

[Trait("Category", TestCategories.Unit)]
public class UsageScoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Recently_used_and_central_scores_hot()
    {
        var inputs = new UsageInputs(LastUsedAt: Now.AddDays(-1).UtcDateTime, Use30d: 15, ReverseDependentCount: 30, false, false);
        var result = UsageScoring.Score(inputs, ContentClass.Cold, lastFlipAt: null, Now);
        Assert.Equal(ContentClass.Hot, result.Class);
        Assert.True(result.Flipped);
    }

    [Fact]
    public void Never_used_scores_cold()
    {
        var inputs = new UsageInputs(LastUsedAt: null, Use30d: 0, ReverseDependentCount: 0, false, false);
        var result = UsageScoring.Score(inputs, ContentClass.Warm, lastFlipAt: null, Now);
        Assert.Equal(ContentClass.Cold, result.Class);
    }

    [Fact]
    public void Pin_hot_and_force_cold_override_the_score()
    {
        var cold = new UsageInputs(null, 0, 0, IsPinnedHot: true, false);
        Assert.Equal(ContentClass.Hot, UsageScoring.Score(cold, ContentClass.Cold, null, Now).Class);

        var hot = new UsageInputs(Now.UtcDateTime, 100, 100, false, IsForcedCold: true);
        Assert.Equal(ContentClass.Cold, UsageScoring.Score(hot, ContentClass.Hot, null, Now).Class);
    }

    [Fact]
    public void Hysteresis_cooldown_blocks_an_early_flip()
    {
        var inputs = new UsageInputs(Now.AddDays(-1).UtcDateTime, 15, 30, false, false); // would be Hot
        // Last flipped 3 days ago (< 7-day cooldown) → stays in its previous class.
        var result = UsageScoring.Score(inputs, ContentClass.Cold, lastFlipAt: Now.AddDays(-3).UtcDateTime, Now);
        Assert.Equal(ContentClass.Cold, result.Class);
        Assert.False(result.Flipped);
    }

    [Fact]
    public void Hysteresis_deadband_keeps_class_stable_near_the_boundary()
    {
        // A score just below the hot threshold shouldn't pull a Warm package up to Hot.
        var inputs = new UsageInputs(Now.AddDays(-30).UtcDateTime, 6, 10, false, false);
        var warm = UsageScoring.Score(inputs, ContentClass.Warm, lastFlipAt: Now.AddDays(-30).UtcDateTime, Now);
        Assert.NotEqual(ContentClass.Hot, warm.Class);
    }

    [Fact]
    public void Score_is_reproducible_from_the_same_inputs()
    {
        var inputs = new UsageInputs(Now.AddDays(-10).UtcDateTime, 8, 12, false, false);
        var a = UsageScoring.Score(inputs, ContentClass.Cold, null, Now);
        var b = UsageScoring.Score(inputs, ContentClass.Cold, null, Now);
        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.Class, b.Class);
    }
}

[Trait("Category", TestCategories.Unit)]
public class PlacementPolicyTests
{
    [Theory]
    [InlineData(ContentClass.Hot, 1)]
    [InlineData(ContentClass.Warm, 2)]
    [InlineData(ContentClass.Cold, 3)]
    public void Desired_tier_follows_class(ContentClass cls, int tier)
    {
        Assert.Equal(tier, PlacementPolicy.DesiredTier(cls));
    }

    [Fact]
    public void Misplaced_when_actual_tier_differs_from_desired()
    {
        Assert.True(PlacementPolicy.IsMisplaced(ContentClass.Hot, actualTier: 3));  // hot var stuck on cold tier
        Assert.False(PlacementPolicy.IsMisplaced(ContentClass.Hot, actualTier: 1));
    }
}
