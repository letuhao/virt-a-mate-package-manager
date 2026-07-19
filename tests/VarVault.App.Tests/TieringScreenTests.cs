using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SCR-5 · Tiering screen shows class counts + misplaced list. (16-checklist SCR-5.)</summary>
[Trait("Category", TestCategories.Unit)]
public class TieringScreenTests
{
    private sealed class StubTiering : ITieringService
    {
        public Task<TierClassCounts> ClassCountsAsync(CancellationToken ct = default) => Task.FromResult(new TierClassCounts(8410, 19240, 42010));
        public Task<IReadOnlyList<MisplacedItem>> MisplacedAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MisplacedItem>>([new MisplacedItem(1, "VeeRifter.CyberRoom.4", "Hot", 3, 1, 880, "hot on slower tier")]);
        public Task<TierMigrationPlan> BuildPlanAsync(CancellationToken ct = default) =>
            Task.FromResult(new TierMigrationPlan([new TierMoveProposal(1, 3, 1)], 0));
    }

    [AvaloniaFact]
    public async Task Shows_counts_and_misplaced()
    {
        var vm = new TieringViewModel(new StubTiering());
        await vm.LoadAsync();
        Assert.Equal(8410, vm.Counts!.Hot);
        Assert.Single(vm.Misplaced);

        var view = new TieringView { DataContext = vm };
        var window = new Window { Width = 800, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("VeeRifter.CyberRoom.4", texts);
        Assert.Contains("8410", texts);
    }
}
