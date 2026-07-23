using VarVault.App.ViewModels;
using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

[Trait("Category", TestCategories.Unit)]
public sealed class EditMetaViewModelTests
{
    private sealed class StubMeta : IVarMetaEditService
    {
        public List<string> Deps { get; set; } = ["A.B.1", "C.D.2"];
        public VarMetaEditRequest? LastSave { get; private set; }

        public Task<Result<VarMetaEditDraft>> LoadAsync(long packageId, long? varFileId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new VarMetaEditDraft(
                packageId, 9, "P.Q.1", @"C:\P.Q.1.var", "P", "Q", "CC", "d", "1", Deps, ["warn"])));

        public Task<Result<VarMetaEditResult>> SaveAsync(VarMetaEditRequest request, CancellationToken cancellationToken = default)
        {
            LastSave = request;
            return Task.FromResult(Result.Success(new VarMetaEditResult(request.PackageId, 9, @"C:\P.Q.1.var", "t1", request.DependencyRefs.Count)));
        }
    }

    [Fact]
    public async Task Paging_and_filter_limit_visible_deps()
    {
        var stub = new StubMeta
        {
            Deps = Enumerable.Range(1, 120).Select(i => $"Creator.Pack{i}.1").ToList(),
        };
        var vm = new EditMetaViewModel(stub, 1);
        await vm.LoadAsync();
        Assert.Equal(EditMetaViewModel.DepsPageSize, vm.VisibleDeps.Count);
        Assert.True(vm.DepsPageCount > 1);

        vm.DepsFilter = "Pack100";
        Assert.Single(vm.VisibleDeps);
        Assert.Equal("Creator.Pack100.1", vm.VisibleDeps[0].Ref);
    }

    [Fact]
    public async Task Load_does_not_mark_dirty()
    {
        var stub = new StubMeta();
        var vm = new EditMetaViewModel(stub, 1);
        await vm.LoadAsync();
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanSave);
    }

    [Fact]
    public async Task Save_sends_edited_dependency_list()
    {
        var stub = new StubMeta();
        var closed = false;
        var vm = new EditMetaViewModel(stub, 1, close: () => closed = true);
        await vm.LoadAsync();
        vm.NewDepRef = "X.Y.3";
        vm.AddDepCommand.Execute(null);
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanSave);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(stub.LastSave);
        Assert.Contains("X.Y.3", stub.LastSave!.DependencyRefs);
        Assert.True(closed);
    }
}
