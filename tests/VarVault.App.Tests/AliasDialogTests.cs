using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>DLG-5 · Alias save persists via IAliasService. (16-checklist DLG-5.)</summary>
[Trait("Category", TestCategories.Unit)]
public class AliasDialogTests
{
    private sealed class StubAlias : IAliasService
    {
        public bool Saved;
        public Task<Result> SetAsync(string missingRef, long owned, CancellationToken ct = default) { Saved = true; return Task.FromResult(Result.Success()); }
        public Task<IReadOnlyList<AliasDto>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AliasDto>>([]);
        public Task<Result> RemoveAsync(long id, CancellationToken ct = default) => Task.FromResult(Result.Success());
    }

    [Fact]
    public async Task Save_calls_set_and_reports()
    {
        var stub = new StubAlias();
        var vm = new AliasViewModel(stub) { MissingRef = "Gone.Missing.1", OwnedPackageId = 2 };
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(stub.Saved);
        Assert.Equal("Alias saved", vm.StatusMessage);
    }
}
