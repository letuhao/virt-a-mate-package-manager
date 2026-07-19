using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.Controls;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SC-6 · MeterBar/StackedBar: fill width tracks value; stacked renders proportional segments. (16-checklist SC-6.)</summary>
[Trait("Category", TestCategories.Unit)]
public class BarsControlTests
{
    [AvaloniaFact]
    public void Meter_fill_tracks_value()
    {
        var bar = new MeterBar { Value = 0.7, Fill = Brushes.Orange, Width = 200 };
        var window = new Window { Content = bar };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var track = bar.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "PART_Track");
        Assert.Equal(2, track.ColumnDefinitions.Count);
        Assert.Equal(0.7, track.ColumnDefinitions[0].Width.Value, 3); // filled star weight
        Assert.Equal(0.3, track.ColumnDefinitions[1].Width.Value, 3);
    }

    [AvaloniaFact]
    public void Meter_clamps_out_of_range_value()
    {
        var bar = new MeterBar { Value = 1.5 };
        Assert.Equal(1.0, bar.FillFraction);
        bar.Value = -0.2;
        Assert.Equal(0.0, bar.FillFraction);
    }

    [AvaloniaFact]
    public void Stacked_renders_proportional_segments()
    {
        var bar = new StackedBar
        {
            Width = 300,
            Segments = new[]
            {
                new BarSegment(0.12, Brushes.Orange),
                new BarSegment(0.28, Brushes.Goldenrod),
                new BarSegment(0.60, Brushes.SteelBlue),
            },
        };
        var window = new Window { Content = bar };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, bar.SegmentCount);
        var track = bar.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "PART_Track");
        Assert.Equal(3, track.ColumnDefinitions.Count);
        Assert.Equal(0.60, track.ColumnDefinitions[2].Width.Value, 3);
    }
}
