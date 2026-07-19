using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.Controls;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SH-2 · Rail nav renders 13 grouped items with badges; clicking navigates. (16-checklist SH-2.)</summary>
[Trait("Category", TestCategories.Unit)]
public class RailViewTests
{
    private static (Window, ShellViewModel, RailView) Show()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>());
        shell.SetBadge("proposals", 7);
        shell.SetBadge("health", 725);
        shell.SetBadge("missing", 1203);
        var rail = new RailView { DataContext = shell };
        var window = new Window { Content = rail };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, shell, rail);
    }

    [AvaloniaFact]
    public void Renders_13_nav_items_and_group_headers()
    {
        var (_, _, rail) = Show();
        var items = rail.GetVisualDescendants().OfType<RailNavItem>().ToList();
        Assert.Equal(13, items.Count);

        var headers = rail.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        foreach (var g in new[] { "Browse", "Optimize", "Problems", "System" })
            Assert.Contains(g, headers);
    }

    [AvaloniaFact]
    public void Badges_bind_to_problem_screens()
    {
        var (_, _, rail) = Show();
        var proposals = rail.GetVisualDescendants().OfType<RailNavItem>().First(r => r.Label == "Proposals");
        Assert.Equal(7, proposals.Badge);
        Assert.True(proposals.HasBadge);
    }

    [AvaloniaFact]
    public void Clicking_a_rail_item_navigates()
    {
        var (_, shell, rail) = Show();
        var settings = rail.GetVisualDescendants().OfType<RailNavItem>().First(r => r.Label == "Settings");
        var button = settings.GetVisualAncestors().OfType<Button>().First();
        Assert.NotNull(button.Command); // the NavigateCommand binding resolved
        button.Command!.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("settings", shell.ActiveScreenId);
        Assert.True(settings.IsActive);
    }
}
