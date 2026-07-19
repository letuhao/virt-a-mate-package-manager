using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>DLG-6 · Confirm-delete protects single-copy items. (16-checklist DLG-6.)</summary>
[Trait("Category", TestCategories.Unit)]
public class ConfirmDeleteDialogTests
{
    private sealed class SpyActions : ILibraryActionService
    {
        public IReadOnlyList<long>? Deleted;
        public Task<BulkActionResult> DeleteAsync(IReadOnlyList<long> ids, CancellationToken ct = default) { Deleted = ids; return Task.FromResult(new BulkActionResult(ids.Count, 0)); }
        public Task<BulkActionResult> AddToPresetAsync(long p, IReadOnlyList<long> ids, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BulkActionResult> FixEncodingAsync(IReadOnlyList<long> ids, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> ExportTxtAsync(IReadOnlyList<long> ids, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BulkActionResult> MoveToSubfolderAsync(IReadOnlyList<long> v, string s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TxtResolveResult> ResolveTxtAsync(string t, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Single_copy_is_excluded_from_delete()
    {
        var spy = new SpyActions();
        var vm = new ConfirmDeleteViewModel(spy);
        vm.SetItems([new(1, "Dupe.Copy.2", false), new(2, "OldLook.v1.1", false), new(3, "Rare.RealLook.3", true)]);

        Assert.Equal(1, vm.ProtectedCount);
        Assert.Equal(2, vm.SafeCount);

        await vm.DeleteCommand.ExecuteAsync(null);
        Assert.Equal([1L, 2L], spy.Deleted); // the single-copy id 3 is never passed to delete
        Assert.Contains("Moved 2", vm.ResultMessage);
    }
}
