using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

[Trait("Category", TestCategories.Unit)]
public class ManageAliasesViewModelTests
{
    private sealed class StubAlias : IAliasService
    {
        public List<AliasDto> Items { get; } = [new(1, "Gone.A.1", "Owned.A.1", 10)];
        public List<(string Missing, long Owned)> Sets { get; } = [];
        public List<long> Removed { get; } = [];

        public Task<Result> SetAsync(string missingRef, long owned, CancellationToken ct = default)
        {
            Sets.Add((missingRef, owned));
            Items.RemoveAll(a => a.MissingRef == missingRef);
            Items.Add(new AliasDto(Items.Count + 2, missingRef, $"Pkg.{owned}", owned));
            return Task.FromResult(Result.Success());
        }

        public Task<IReadOnlyList<AliasDto>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AliasDto>>(Items.ToList());

        public Task<Result> RemoveAsync(long id, CancellationToken ct = default)
        {
            Removed.Add(id);
            Items.RemoveAll(a => a.Id == id);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class StubClipboard : VarVault.App.Services.IClipboard
    {
        public string? Last;
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Last = text;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Load_lists_aliases()
    {
        var stub = new StubAlias();
        var vm = new ManageAliasesViewModel(stub);
        await vm.LoadAsync();
        Assert.Single(vm.Items);
        Assert.Equal("Gone.A.1", vm.Items[0].MissingRef);
    }

    [Fact]
    public async Task Link_and_unlink_raise_OnChanged()
    {
        var stub = new StubAlias();
        var changed = 0;
        var vm = new ManageAliasesViewModel(stub) { OnChanged = () => changed++ };
        await vm.LoadAsync();
        vm.MissingRef = "Ghost.B.2";
        vm.OwnedPackageId = 99;
        await vm.LinkCommand.ExecuteAsync(null);
        Assert.Contains(stub.Sets, s => s.Missing == "Ghost.B.2" && s.Owned == 99);
        Assert.Equal(1, changed);

        vm.SelectedAlias = vm.Items.First(a => a.MissingRef == "Ghost.B.2");
        var id = vm.SelectedAlias.Id;
        await vm.UnlinkCommand.ExecuteAsync(null);
        Assert.Contains(id, stub.Removed);
        Assert.Equal(2, changed);
    }

    [Fact]
    public async Task Copy_missing_uses_clipboard()
    {
        var clip = new StubClipboard();
        var vm = new ManageAliasesViewModel(new StubAlias(), clipboard: clip) { MissingRef = "Copy.Me.1" };
        await vm.CopyMissingCommand.ExecuteAsync(null);
        Assert.Equal("Copy.Me.1", clip.Last);
    }
}
