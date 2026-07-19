using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>DLG-8 · Keep one, trash the rest of a duplicate group. (16-checklist DLG-8.)</summary>
[Trait("Category", TestCategories.Unit)]
public class DupeReviewDialogTests
{
    private sealed class SpyReclaim : IReclaimService
    {
        public long Keep; public IReadOnlyList<long>? Trash;
        public Task<ReclaimResult> TrashRedundantAsync(long keep, IReadOnlyList<long> trash, CancellationToken ct = default)
        { Keep = keep; Trash = trash; return Task.FromResult(new ReclaimResult(trash.Count, 0)); }
        public Task<IReadOnlyList<DuplicateGroup>> ExactGroupsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DuplicateGroup>>([]);
    }

    [Fact]
    public async Task Keep_first_trash_rest()
    {
        var spy = new SpyReclaim();
        var vm = new DupeReviewViewModel(spy);
        vm.SetGroup(new DuplicateGroup("KEY", "sig", [new DuplicateCopy(1,1,"a",true,100), new DuplicateCopy(2,3,"b",true,100), new DuplicateCopy(3,3,"c",true,100)]));
        Assert.Equal(1, vm.KeepId);

        await vm.KeepAndTrashCommand.ExecuteAsync(null);
        Assert.Equal(1, spy.Keep);
        Assert.Equal([2L, 3L], spy.Trash);
        Assert.Contains("trashed 2", vm.ResultMessage);
    }
}
