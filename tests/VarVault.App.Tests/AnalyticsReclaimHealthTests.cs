using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

// (doc 26 · G-7) The Reclaim/DedupReview/HealthReport orphan ViewModels were deleted; Analytics is a live screen.
[Trait("Category", TestCategories.Unit)]
public class AnalyticsViewModelTests
{
    [Fact]
    public async Task Refresh_fills_all_three_breakdowns()
    {
        var vm = new AnalyticsViewModel(new FakeAnalytics());
        await vm.RefreshAsync();
        Assert.Single(vm.ByCreator);
        Assert.Single(vm.ByType);
        Assert.Single(vm.ByTier);
    }

    private sealed class FakeAnalytics : IAnalyticsService
    {
        private static Task<IReadOnlyList<SpaceByGroup>> One(string g) =>
            Task.FromResult<IReadOnlyList<SpaceByGroup>>([new SpaceByGroup(g, 1000, 1)]);
        public Task<IReadOnlyList<SpaceByGroup>> SpaceByCreatorAsync(CancellationToken ct = default) => One("Alice");
        public Task<IReadOnlyList<SpaceByGroup>> SpaceByTypeAsync(CancellationToken ct = default) => One("Scene");
        public Task<IReadOnlyList<SpaceByGroup>> SpaceByTierAsync(CancellationToken ct = default) => One("Tier 1");
    }
}
