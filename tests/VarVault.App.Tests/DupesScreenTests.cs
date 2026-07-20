using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SCR-6 · Duplicates screen lists exact groups + reclaimable total. (16-checklist SCR-6.)</summary>
[Trait("Category", TestCategories.Unit)]
public class DupesScreenTests
{
    private sealed class StubReclaim : IReclaimService
    {
        public Task<IReadOnlyList<DuplicateGroup>> ExactGroupsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DuplicateGroup>>(
            [
                new DuplicateGroup("HUNTING.EYES.6", "sig", [new DuplicateCopy(1,1,"a",true,384), new DuplicateCopy(2,3,"b",true,384)]),
            ]);
        public Task<IReadOnlyList<NearDuplicateGroup>> NearDuplicateGroupsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NearDuplicateGroup>>([]);
        public Task<ReclaimResult> TrashRedundantAsync(long keep, IReadOnlyList<long> trash, CancellationToken ct = default) =>
            Task.FromResult(new ReclaimResult(trash.Count, 0));
    }

    [AvaloniaFact]
    public async Task Lists_groups_and_reclaimable()
    {
        var vm = new DupesViewModel(new StubReclaim());
        await vm.LoadAsync();
        Assert.Single(vm.Groups);
        Assert.Equal(384, vm.ReclaimableBytes); // one redundant copy

        var view = new DupesView { DataContext = vm };
        var window = new Window { Width = 700, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.SelectedTabIndex = 1; // the groups table lives on the "Exact duplicates" tab now (24-checklist A8)
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("HUNTING.EYES.6", view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    }
}
