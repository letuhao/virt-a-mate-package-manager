using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>DLG-3 · Migrate dialog shows plan + single-copy exclusions. (16-checklist DLG-3.)</summary>
[Trait("Category", TestCategories.Unit)]
public class MigrateDialogTests
{
    private sealed class StubTiering : ITieringService
    {
        public Task<TierClassCounts> ClassCountsAsync(CancellationToken ct = default) => Task.FromResult(new TierClassCounts(0, 0, 0));
        public Task<IReadOnlyList<MisplacedItem>> MisplacedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MisplacedItem>>([]);
        public Task<TierMigrationPlan> BuildPlanAsync(CancellationToken ct = default) =>
            Task.FromResult(new TierMigrationPlan([new TierMoveProposal(1, 3, 1), new TierMoveProposal(2, 3, 1)], ExcludedCount: 2));
        public Task<TierPolicy> PolicyAsync(CancellationToken ct = default) => Task.FromResult(new TierPolicy([]));
        public Task<IReadOnlyList<StaleVersion>> StaleVersionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<StaleVersion>>([]);
    }

    [Fact]
    public async Task Loads_plan_and_flags_exclusions()
    {
        var vm = new MigrateViewModel(new StubTiering());
        await vm.LoadPlanCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Moves.Count);
        Assert.True(vm.HasExclusions);
        Assert.Contains("copy", vm.Flow);
    }
}
