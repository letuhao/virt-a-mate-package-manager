using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SH-1 · Shell navigation swaps the active screen; rail list is grouped. (16-checklist SH-1.)</summary>
[Trait("Category", TestCategories.Unit)]
public class ShellViewModelTests
{
    [Fact]
    public void Navigate_swaps_active_screen()
    {
        var library = new object();
        var settings = new object();
        var shell = new ShellViewModel(new Dictionary<string, object> { ["library"] = library, ["settings"] = settings });

        Assert.Equal("library", shell.ActiveScreenId);   // initial
        Assert.Same(library, shell.ActiveScreen);

        shell.NavigateCommand.Execute("settings");
        Assert.Equal("settings", shell.ActiveScreenId);
        Assert.Same(settings, shell.ActiveScreen);
    }

    [Fact]
    public void Rail_lists_all_13_screens_in_four_groups()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>());
        Assert.Equal(14, shell.Screens.Count);
        Assert.Equal(["Browse", "Optimize", "Problems", "System"], shell.Screens.Select(s => s.Group).Distinct());
    }

    [Fact]
    public void Navigate_to_unregistered_screen_sets_id_but_null_screen()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>());
        shell.NavigateCommand.Execute("analytics");
        Assert.Equal("analytics", shell.ActiveScreenId);
        Assert.Null(shell.ActiveScreen);
    }
}
