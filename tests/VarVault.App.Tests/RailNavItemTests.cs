using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.Controls;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SC-11 · RailNavItem: label + optional badge + active state. (16-checklist SC-11.)</summary>
[Trait("Category", TestCategories.Unit)]
public class RailNavItemTests
{
    [AvaloniaFact]
    public void Active_toggles_class()
    {
        var item = new RailNavItem { Label = "Proposals" };
        Assert.DoesNotContain("active", item.Classes);
        item.IsActive = true;
        Assert.Contains("active", item.Classes);
        item.IsActive = false;
        Assert.DoesNotContain("active", item.Classes);
    }

    [AvaloniaFact]
    public void Badge_count_renders_when_present()
    {
        var item = new RailNavItem { Label = "Health", Badge = 725, BadgeKind = BadgeKind.Warn };
        var window = new Window { Content = item };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(item.HasBadge);
        Assert.Contains("warn", item.Classes);
        var badge = item.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Badge");
        Assert.True(badge.IsVisible);
        Assert.Contains("725", item.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    }

    [AvaloniaFact]
    public void No_badge_hides_the_badge()
    {
        var item = new RailNavItem { Label = "Library" };
        var window = new Window { Content = item };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(item.HasBadge);
        var badge = item.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Badge");
        Assert.False(badge.IsVisible);
    }
}
