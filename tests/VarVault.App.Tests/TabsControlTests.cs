using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.Controls;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SC-4 · Tabs: single active tab, count badges, click switches active + swaps content. (16-checklist SC-4.)</summary>
[Trait("Category", TestCategories.Unit)]
public class TabsControlTests
{
    private static Tabs Build()
    {
        var tabs = new Tabs
        {
            ItemsSource = new[]
            {
                new TabItemModel("Encoding", 684),
                new TabItemModel("Integrity", 41),
                new TabItemModel("Missing meta", 12),
            },
        };
        return tabs;
    }

    [AvaloniaFact]
    public void Click_switches_active_tab_and_selected_item()
    {
        var tabs = Build();
        var window = new Window { Content = tabs };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var buttons = tabs.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(3, buttons.Count);
        Assert.Equal(0, tabs.SelectedIndex); // first active by default

        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Equal("Integrity", ((TabItemModel)tabs.SelectedItem!).Label);
        Assert.Contains("active", buttons[1].Classes);
        Assert.DoesNotContain("active", buttons[0].Classes);
    }

    [AvaloniaFact]
    public void Count_badges_render()
    {
        var tabs = Build();
        var window = new Window { Content = tabs };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = tabs.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Encoding", texts);
        Assert.Contains("684", texts); // count badge
    }

    [AvaloniaFact]
    public void Selected_item_swaps_when_index_changes()
    {
        var tabs = Build();
        var swaps = new List<object?>();
        tabs.PropertyChanged += (_, e) => { if (e.Property == Tabs.SelectedItemProperty) swaps.Add(e.NewValue); };
        var window = new Window { Content = tabs };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        tabs.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        // SelectedItem tracks the tab — this is the seam a screen binds its content to.
        Assert.Equal("Missing meta", ((TabItemModel)tabs.SelectedItem!).Label);
        Assert.Contains(swaps, v => v is TabItemModel { Label: "Missing meta" });
    }
}
