using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SH-5 · Log dock shows index status, selected count, and tier %. (16-checklist SH-5.)</summary>
[Trait("Category", TestCategories.Unit)]
public class LogDockViewTests
{
    [AvaloniaFact]
    public void Binds_index_selected_and_tier_summary()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>())
        {
            IndexStatus = "Indexing T3 · 18,204/26,455 · stage 2",
            SelectedCount = 3,
            TierSummary = "Storage T1 70% · T2 95% · T3 39%",
        };
        var dock = new LogDockView { DataContext = shell };
        var window = new Window { Content = dock };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = dock.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Indexing T3 · 18,204/26,455 · stage 2", texts);
        Assert.Contains("selected 3", texts);
        Assert.Contains("Storage T1 70% · T2 95% · T3 39%", texts);
    }
}
