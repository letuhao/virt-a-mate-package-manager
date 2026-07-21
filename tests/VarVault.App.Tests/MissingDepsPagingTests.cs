using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Missing-deps pager commands: page size, navigation, and independent totals.</summary>
[Trait("Category", TestCategories.Unit)]
public sealed class MissingDepsPagingTests
{
    private sealed class FakeQuery : IMissingDepsQuery
    {
        private readonly List<MissingDependency> _items =
            Enumerable.Range(1, 120)
                .Select(i => new MissingDependency($"Ghost.Pkg{i}.1", 200 - i))
                .ToList();

        public Task<PageResult<MissingDependency>> GetPageAsync(
            PageRequest request, string? searchText = null, CancellationToken cancellationToken = default)
        {
            var page = request.Normalize();
            return Task.FromResult(new PageResult<MissingDependency>(
                _items.Skip(page.Skip).Take(page.SafePageSize).ToList(),
                _items.Count,
                page.SafePageNumber,
                page.SafePageSize));
        }

        public Task<IReadOnlyList<MissingDependency>> GetMissingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MissingDependency>>(_items);
    }

    [Fact]
    public async Task Refresh_loads_first_page_of_fifty_with_exact_total()
    {
        var vm = new MissingDepsViewModel(new FakeQuery());
        await vm.RefreshAsync();
        Assert.Equal(50, vm.Items.Count);
        Assert.Equal(120, vm.TotalMissing);
        Assert.Equal(1, vm.Pager.PageNumber);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task Next_and_page_size_commands_change_the_visible_window()
    {
        var vm = new MissingDepsViewModel(new FakeQuery());
        await vm.RefreshAsync();
        await vm.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Pager.PageNumber);
        Assert.Equal(50, vm.Items.Count);
        Assert.StartsWith("Ghost.Pkg51", vm.Items[0].Ref, StringComparison.Ordinal);

        await vm.ChangePageSizeCommand.ExecuteAsync(25);
        Assert.Equal(1, vm.Pager.PageNumber);
        Assert.Equal(25, vm.Items.Count);
        Assert.Equal(25, vm.Pager.PageSize);
    }
}
