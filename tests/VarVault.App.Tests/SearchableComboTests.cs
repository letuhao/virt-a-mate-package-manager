using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.Controls;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SC-8 · SearchableCombo: type-to-filter with counts, ↑/↓ highlight, Enter picks, Esc closes. (16-checklist SC-8.)</summary>
[Trait("Category", TestCategories.Unit)]
public class SearchableComboTests
{
    private static SearchableCombo Build() => new()
    {
        ItemsSource = new[]
        {
            new ComboOption("MeshedVR", 412),
            new ComboOption("DaniPunani", 241),
            new ComboOption("NoStage3", 140),
            new ComboOption("AshAuryn", 88),
            new ComboOption("Chokaphi", 27),
        },
    };

    [AvaloniaFact]
    public void Filter_narrows_the_list()
    {
        var combo = Build();
        combo.Refilter();
        Assert.Equal(5, combo.FilteredOptions.Count);

        combo.FilterText = "an"; // only DaniPunani contains "an"
        Assert.Single(combo.FilteredOptions);
        Assert.Equal("DaniPunani", combo.FilteredOptions[0].Name);
    }

    [AvaloniaFact]
    public void Empty_filter_sets_empty_state()
    {
        var combo = Build();
        combo.FilterText = "zzz";
        Assert.True(combo.IsEmpty);
        Assert.Empty(combo.FilteredOptions);
        Assert.Equal(-1, combo.HighlightedIndex);
    }

    [AvaloniaFact]
    public void Arrows_move_highlight_and_enter_picks()
    {
        var combo = Build();
        combo.Open();                       // full list, highlight 0
        Assert.True(combo.IsOpen);
        combo.MoveHighlight(1);
        combo.MoveHighlight(1);             // index 2
        Assert.Equal(2, combo.HighlightedIndex);
        combo.MoveHighlight(50);            // clamps
        Assert.Equal(4, combo.HighlightedIndex);

        combo.HighlightedIndex = 1;
        combo.CommitHighlighted();
        Assert.Equal("DaniPunani", combo.SelectedName);
        Assert.False(combo.IsOpen);         // Enter closes
    }

    [AvaloniaFact]
    public void Keyboard_map_down_then_enter_selects()
    {
        var combo = Build();
        combo.Open();                       // full list, highlight 0
        combo.HandleKey(Key.Down);          // -> 1
        combo.HandleKey(Key.Enter);         // pick + close

        Assert.Equal("DaniPunani", combo.SelectedName);
        Assert.False(combo.IsOpen);
    }

    [AvaloniaFact]
    public void Escape_closes()
    {
        var combo = Build();
        combo.Open();
        Assert.True(combo.IsOpen);
        combo.Close();
        Assert.False(combo.IsOpen);
    }
}
