using VarVault.Domain.Analyzer;
using VarVault.Domain.Entities;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class MigrationPlannerTests
{
    private static readonly Func<int, bool> AllTargetsSafe = _ => true;

    [Fact]
    public void Proposes_moving_a_hot_var_off_a_cold_tier()
    {
        // Hot class currently on tier 3 (cold) → propose move to tier 1.
        var candidates = new[] { new MigrationCandidate(1, CurrentTier: 3, ContentClass.Hot, IsOnline: true, IsSingleCopy: false) };
        var plan = MigrationPlanner.Plan(candidates, AllTargetsSafe);

        var proposal = Assert.Single(plan.Proposals);
        Assert.Equal(3, proposal.FromTier);
        Assert.Equal(1, proposal.ToTier);
        Assert.Empty(plan.Excluded);
    }

    [Fact]
    public void Correctly_placed_copies_produce_no_proposal()
    {
        var candidates = new[] { new MigrationCandidate(1, CurrentTier: 1, ContentClass.Hot, true, false) };
        var plan = MigrationPlanner.Plan(candidates, AllTargetsSafe);
        Assert.Empty(plan.Proposals);
        Assert.Empty(plan.Excluded);
    }

    [Fact]
    public void Excludes_single_copy_offline_and_unsafe_targets()
    {
        var candidates = new[]
        {
            new MigrationCandidate(1, 3, ContentClass.Hot, IsOnline: false, IsSingleCopy: false), // offline
            new MigrationCandidate(2, 3, ContentClass.Hot, IsOnline: true, IsSingleCopy: true),   // single copy
            new MigrationCandidate(3, 3, ContentClass.Hot, IsOnline: true, IsSingleCopy: false),  // unsafe target
        };
        var plan = MigrationPlanner.Plan(candidates, targetTierIsSafe: tier => false); // no safe target

        Assert.Empty(plan.Proposals);
        Assert.Contains(plan.Excluded, e => e.VarFileId == 1 && e.Reason == MigrationExcludeReason.Offline);
        Assert.Contains(plan.Excluded, e => e.VarFileId == 2 && e.Reason == MigrationExcludeReason.SingleCopy);
        Assert.Contains(plan.Excluded, e => e.VarFileId == 3 && e.Reason == MigrationExcludeReason.UnsafeTarget);
    }

    [Fact]
    public void Planner_only_proposes_and_never_returns_an_executed_action()
    {
        // BE-P5: the planner's output is proposals only — there is no execute path in it.
        var candidates = new[] { new MigrationCandidate(1, 3, ContentClass.Hot, true, false) };
        var plan = MigrationPlanner.Plan(candidates, AllTargetsSafe);
        Assert.All(plan.Proposals, p => Assert.NotEqual(p.FromTier, p.ToTier));
    }
}
