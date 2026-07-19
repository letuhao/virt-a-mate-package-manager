using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.Controls;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SC-5 · StatePill / Chip / Tag. State is carried by text (colourblind-safe), not colour alone. (16-checklist SC-5.)</summary>
[Trait("Category", TestCategories.Unit)]
public class PillsControlTests
{
    [AvaloniaFact]
    public void State_pill_carries_meaning_in_text_per_state()
    {
        var ok = new StatePill { State = StateKind.Ok, Text = "ready" };
        var sub = new StatePill { State = StateKind.Sub, Text = "substituted" };
        var miss = new StatePill { State = StateKind.Miss, Text = "missing" };
        var window = new Window { Content = new StackPanel { Children = { ok, sub, miss } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("ok", ok.Classes);
        Assert.Contains("sub", sub.Classes);
        Assert.Contains("miss", miss.Classes);
        // Distinct text labels — meaning is readable without colour.
        Assert.Equal("ready", ok.GetVisualDescendants().OfType<TextBlock>().First().Text);
        Assert.Equal("substituted", sub.GetVisualDescendants().OfType<TextBlock>().First().Text);
        Assert.Equal("missing", miss.GetVisualDescendants().OfType<TextBlock>().First().Text);
    }

    [AvaloniaFact]
    public void State_pill_defaults_text_to_state_name()
    {
        var pill = new StatePill { State = StateKind.Miss };
        var window = new Window { Content = pill };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("miss", pill.GetVisualDescendants().OfType<TextBlock>().First().Text);
    }

    [AvaloniaFact]
    public void Chip_active_toggles_class()
    {
        var chip = new Chip { Content = "Installed" };
        Assert.DoesNotContain("active", chip.Classes);
        chip.IsActive = true;
        Assert.Contains("active", chip.Classes);
    }

    [AvaloniaFact]
    public void Tag_kind_sets_class()
    {
        Assert.Contains("good", new Tag { Kind = TagKind.Good }.Classes);
        Assert.Contains("warn", new Tag { Kind = TagKind.Warn }.Classes);
        Assert.Contains("crit", new Tag { Kind = TagKind.Crit }.Classes);
        Assert.Empty(new Tag { Kind = TagKind.Default }.Classes);
    }
}
