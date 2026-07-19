using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SCR-7 · Analytics screen renders space-by-type/creator. (16-checklist SCR-7.)</summary>
[Trait("Category", TestCategories.Unit)]
public class AnalyticsScreenTests
{
    private sealed class StubAnalytics : IAnalyticsService
    {
        public Task<IReadOnlyList<SpaceByGroup>> SpaceByCreatorAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SpaceByGroup>>([new SpaceByGroup("Spacedog", 210, 5)]);
        public Task<IReadOnlyList<SpaceByGroup>> SpaceByTypeAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SpaceByGroup>>([new SpaceByGroup("Assets", 1600, 12)]);
        public Task<IReadOnlyList<SpaceByGroup>> SpaceByTierAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SpaceByGroup>>([]);
    }

    [AvaloniaFact]
    public async Task Renders_space_breakdowns()
    {
        var vm = new AnalyticsViewModel(new StubAnalytics());
        await vm.RefreshAsync();
        var view = new AnalyticsView { DataContext = vm };
        var window = new Window { Width = 800, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Assets", texts);
        Assert.Contains("Spacedog", texts);
    }
}
