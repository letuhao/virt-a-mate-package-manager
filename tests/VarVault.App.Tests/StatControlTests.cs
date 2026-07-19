using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.Controls;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SC-7 · StatTile renders value+unit; TempDot colours by temperature. (16-checklist SC-7.)</summary>
[Trait("Category", TestCategories.Unit)]
public class StatControlTests
{
    [AvaloniaFact]
    public void Stat_tile_renders_value_and_unit()
    {
        var tile = new StatTile { Value = "590", Unit = "GB recoverable" };
        var window = new Window { Content = tile };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = tile.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("590", texts);
        Assert.Contains("GB recoverable", texts);
    }

    [AvaloniaFact]
    public void Temp_dot_classes_by_temperature()
    {
        Assert.Contains("hot", new TempDot { Temperature = Temperature.Hot }.Classes);
        Assert.Contains("warm", new TempDot { Temperature = Temperature.Warm }.Classes);
        Assert.Contains("cold", new TempDot { Temperature = Temperature.Cold }.Classes);
    }
}
