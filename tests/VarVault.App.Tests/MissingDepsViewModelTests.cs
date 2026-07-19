using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

[Trait("Category", TestCategories.Unit)]
public class MissingDepsViewModelTests
{
    [Fact]
    public async Task Refresh_loads_missing_refs()
    {
        var vm = new MissingDepsViewModel(new FakeQuery(
            new MissingDependency("Ghost.A.1", 3),
            new MissingDependency("Ghost.B.1", 1)));

        await vm.RefreshAsync();

        Assert.Equal(2, vm.Items.Count);
        Assert.False(vm.IsEmpty);
        Assert.Equal("Ghost.A.1", vm.Items[0].Ref);
    }

    [Fact]
    public async Task Empty_when_nothing_missing()
    {
        var vm = new MissingDepsViewModel(new FakeQuery());
        await vm.RefreshAsync();
        Assert.True(vm.IsEmpty);
    }

    private sealed class FakeQuery(params MissingDependency[] items) : IMissingDepsQuery
    {
        public Task<IReadOnlyList<MissingDependency>> GetMissingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MissingDependency>>(items);
    }
}
