using VarVault.Domain.Analyzer;
using VarVault.Domain.Entities;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class RebalancePlannerTests
{
    [Fact]
    public void Hot_var_on_a_cold_tier_moves_to_a_new_hot_tier()
    {
        var candidates = new[]
        {
            new MigrationCandidate(1, CurrentTier: 3, ContentClass.Hot, IsOnline: true, IsSingleCopy: false),  // should move to 1
            new MigrationCandidate(2, CurrentTier: 1, ContentClass.Hot, IsOnline: true, IsSingleCopy: false),  // already on 1
            new MigrationCandidate(3, CurrentTier: 3, ContentClass.Cold, IsOnline: true, IsSingleCopy: false), // cold — wants tier 3
            new MigrationCandidate(4, CurrentTier: 3, ContentClass.Hot, IsOnline: true, IsSingleCopy: true),   // single copy — excluded
        };

        var moving = RebalancePlanner.CandidatesForNewTier(candidates, newTier: 1);
        Assert.Equal([1L], moving);
    }

    [Fact]
    public void Offline_and_single_copy_are_excluded()
    {
        var candidates = new[]
        {
            new MigrationCandidate(1, 3, ContentClass.Hot, IsOnline: false, IsSingleCopy: false),
            new MigrationCandidate(2, 3, ContentClass.Hot, IsOnline: true, IsSingleCopy: true),
        };
        Assert.Empty(RebalancePlanner.CandidatesForNewTier(candidates, 1));
    }
}
