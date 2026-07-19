using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>DLG-11 · Shell toast undo reverses the last action. (16-checklist DLG-11.)</summary>
[Trait("Category", TestCategories.Unit)]
public class ShellToastTests
{
    [Fact]
    public void Undo_reverses_the_last_action()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>());
        var restored = false;
        shell.ShowToast("Moved 2 items to trash", undo: () => restored = true);

        Assert.True(shell.ToastVisible);
        Assert.True(shell.ToastHasUndo);

        shell.UndoToastCommand.Execute(null);
        Assert.True(restored);              // the undo action ran (e.g. trash restore)
        Assert.False(shell.ToastVisible);
    }

    [Fact]
    public void Dismiss_hides_without_undo()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>());
        shell.ShowToast("Indexed 277 vars");
        Assert.False(shell.ToastHasUndo);
        shell.DismissToastCommand.Execute(null);
        Assert.False(shell.ToastVisible);
    }
}
