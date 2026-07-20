using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

// (doc 26 · G-7) The Jobs/Confirm/UndoToast orphan ViewModels were deleted; the command palette is live.
[Trait("Category", TestCategories.Unit)]
public class CommandPaletteViewModelTests
{
    [Fact]
    public void Query_filters_commands_and_invoke_runs()
    {
        var ran = false;
        var vm = new CommandPaletteViewModel(
        [
            new PaletteCommand("Index all repositories", "Index", () => { }),
            new PaletteCommand("Open settings", "App", () => ran = true),
        ]);

        Assert.Equal(2, vm.Results.Count);

        vm.Query = "settings";
        Assert.Single(vm.Results);
        Assert.Equal("Open settings", vm.Results[0].Name);

        vm.Invoke(vm.Results[0]);
        Assert.True(ran);
        Assert.False(vm.IsOpen);
    }
}
