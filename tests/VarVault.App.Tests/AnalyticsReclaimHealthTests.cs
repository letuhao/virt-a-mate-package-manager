using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

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

[Trait("Category", TestCategories.Unit)]
public class ReclaimViewModelTests
{
    [Fact]
    public void Excludes_single_copy_and_sums_reclaimable()
    {
        var vm = new ReclaimViewModel();
        vm.Load(
        [
            new ReclaimItem("dup a", "Duplicate", 1000, IsSingleCopy: false),
            new ReclaimItem("cold b", "ColdOnSSD", 2000, IsSingleCopy: false),
            new ReclaimItem("only copy", "Orphan", 5000, IsSingleCopy: true), // excluded
        ]);

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(3000, vm.TotalReclaimableBytes);
    }
}

[Trait("Category", TestCategories.Unit)]
public class DedupReviewViewModelTests
{
    [Fact]
    public void Keeps_healthiest_online_copy_and_computes_reclaim()
    {
        var group = new DedupGroupViewModel("A.B.1",
        [
            new DedupCopy(1, "HDD", 1000, "CorruptZip", IsOnline: true),
            new DedupCopy(2, "SSD", 1000, "Ok", IsOnline: true),
        ]);
        Assert.Equal(2, group.KeeperVarFileId);         // healthy "Ok" copy kept
        Assert.Equal(1000, group.ReclaimableBytes);      // the corrupt one is removable

        var vm = new DedupReviewViewModel();
        vm.Load([group]);
        Assert.Equal(1000, vm.TotalReclaimableBytes);
    }
}

[Trait("Category", TestCategories.Unit)]
public class HealthReportViewModelTests
{
    [Fact]
    public async Task Groups_by_codepage_and_fix_all_invokes_batch()
    {
        var fixedEntries = 0;
        var vm = new HealthReportViewModel(entries => { fixedEntries += entries.Count; return Task.CompletedTask; });
        vm.Load(
        [
            new HealthEntry(1, "A.B.1", "GBK", 0),
            new HealthEntry(2, "C.D.1", "GBK", 1),
            new HealthEntry(3, "E.F.1", "Shift-JIS", 0),
        ]);

        Assert.Equal(3, vm.TotalBroken);
        Assert.Equal(2, vm.Groups.Count);
        Assert.Equal("GBK", vm.Groups[0].Codepage); // most-broken group first

        await vm.FixAllAsync(vm.Groups[0]);
        Assert.Equal(2, fixedEntries);
    }
}
