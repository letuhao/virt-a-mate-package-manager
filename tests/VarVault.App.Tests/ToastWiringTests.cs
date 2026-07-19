using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// GF-1 · A completed delete raises a shell toast (the OnDeleted seam the launcher wires to
/// ShellViewModel.ShowToast). (18-gap GF-1.)
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ToastWiringTests
{
    private sealed class SpyActions : ILibraryActionService
    {
        public Task<BulkActionResult> AddToPresetAsync(long p, IReadOnlyList<long> ids, CancellationToken ct = default) => Task.FromResult(new BulkActionResult(0, 0));
        public Task<BulkActionResult> FixEncodingAsync(IReadOnlyList<long> ids, CancellationToken ct = default) => Task.FromResult(new BulkActionResult(0, 0));
        public Task<BulkActionResult> DeleteAsync(IReadOnlyList<long> ids, CancellationToken ct = default) => Task.FromResult(new BulkActionResult(ids.Count, 0));
        public Task<string> ExportTxtAsync(IReadOnlyList<long> ids, CancellationToken ct = default) => Task.FromResult("");
        public Task<BulkActionResult> MoveToSubfolderAsync(IReadOnlyList<long> v, string s, CancellationToken ct = default) => Task.FromResult(new BulkActionResult(0, 0));
        public Task<TxtResolveResult> ResolveTxtAsync(string t, CancellationToken ct = default) => Task.FromResult(new TxtResolveResult([], []));
    }

    [Fact]
    public async Task Delete_raises_a_toast_with_the_trashed_count()
    {
        string? toast = null;
        var vm = new ConfirmDeleteViewModel(new SpyActions()) { OnDeleted = n => toast = $"Moved {n} items to trash" };
        vm.SetItems([new ConfirmItem(1, "A.B.1", false), new ConfirmItem(2, "C.D.2", false)]);
        await vm.DeleteCommand.ExecuteAsync(null);
        Assert.Equal("Moved 2 items to trash", toast);
    }

    [Fact]
    public void Shell_show_toast_sets_message_and_undo()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>());
        var undone = false;
        shell.ShowToast("Moved 2 items to trash", () => undone = true);
        Assert.True(shell.ToastVisible);
        Assert.Equal("Moved 2 items to trash", shell.ToastMessage);
        Assert.True(shell.ToastHasUndo);
        shell.UndoToastCommand.Execute(null);
        Assert.True(undone);
        Assert.False(shell.ToastVisible);
    }
}
