using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.ViewModels;
using VarVault.App.Views;

namespace VarVault.App.Tests;

/// <summary>
/// GC-1 · The rail has its branding (logo tile + wordmark + live package count) and every nav item renders
/// an icon. (18-gap GC-1.)
/// </summary>
public class RailChromeTests
{
    [AvaloniaFact]
    public void Every_nav_item_has_an_icon_geometry()
    {
        foreach (var s in ShellViewModel.AllScreens)
            Assert.False(string.IsNullOrWhiteSpace(s.Icon), $"{s.Id} has no icon");
    }

    [AvaloniaFact]
    public void Logo_wordmark_and_package_count_render()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>());
        shell.PackageCountLabel = "69,660 pkgs";
        var rail = new RailView { DataContext = shell };
        var window = new Avalonia.Controls.Window { Content = rail };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = rail.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("VarVault", texts);
        Assert.Contains("69,660 pkgs", texts);

        // 13 nav items → 13 icon paths rendered.
        var icons = rail.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Count();
        Assert.True(icons >= 13, $"expected >=13 nav icons, saw {icons}");
    }
}
