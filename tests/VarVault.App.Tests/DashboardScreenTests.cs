using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SCR-1 · Dashboard loads the summary and renders tiles. (16-checklist SCR-1.)</summary>
[Trait("Category", TestCategories.Unit)]
public class DashboardScreenTests
{
    private sealed class StubDashboard(DashboardSummary summary) : IDashboardService
    {
        public Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default) => Task.FromResult(summary);
    }

    private static DashboardSummary Sample() => new(
        TotalPackages: 69660, TotalBytes: 4_300_000_000, RepositoryCount: 4, OfflineRepositoryCount: 1,
        ActiveInGame: 1847, HotCount: 8410, WarmCount: 19240, ColdCount: 42010, MissingDepsCount: 1203,
        Tiers: [new TierUtilization(1, 1400, 2000, 1), new TierUtilization(3, 3100, 8000, 1)]);

    [AvaloniaFact]
    public async Task Load_populates_summary()
    {
        var vm = new DashboardViewModel(new StubDashboard(Sample()));
        await vm.LoadAsync();
        Assert.NotNull(vm.Summary);
        Assert.Equal(69660, vm.Summary!.TotalPackages);
        Assert.True(vm.HasSummary);
    }

    [AvaloniaFact]
    public async Task View_renders_totals_and_classification()
    {
        var vm = new DashboardViewModel(new StubDashboard(Sample()));
        await vm.LoadAsync();
        var view = new DashboardView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("69660 packages", texts);
        // Classification is now a labeled stacked bar (24-checklist E8): "Hot" label + the count separately.
        Assert.Contains("Hot", texts);
        Assert.Contains("8410", texts);
        // Attention row is now a clickable navigation button (GD-1) → count + arrow.
        Assert.Contains(texts, t => t is not null && t.StartsWith("1203 missing dependencies"));
    }
}
