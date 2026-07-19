using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// View-model presentation logic tested as plain units (no Avalonia): filter/sort state flows into the
/// query, results populate, and incremental "load more" is gated by the total count. (Doc 14 §6.)
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class LibraryViewModelTests
{
    [Fact]
    public async Task Refresh_loads_the_first_page_and_creators()
    {
        var query = new FakeLibraryQuery(total: 3, creators: ["Alice", "Bob"]);
        var vm = new LibraryViewModel(query);

        await vm.RefreshAsync();

        Assert.Equal(3, vm.Items.Count);
        Assert.Equal(3, vm.TotalCount);
        Assert.Equal(["Alice", "Bob"], vm.Creators);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Filter_and_sort_state_flow_into_the_query()
    {
        var query = new FakeLibraryQuery(total: 1, creators: []);
        var vm = new LibraryViewModel(query) { CreatorFilter = "Alice", FavoritesOnly = true, Sort = LibrarySort.Size, Descending = true };

        await vm.RefreshAsync();

        Assert.Equal("Alice", query.LastQuery!.Creator);
        Assert.True(query.LastQuery.FavoritesOnly);
        Assert.Equal(LibrarySort.Size, query.LastQuery.Sort);
        Assert.True(query.LastQuery.Descending);
    }

    [Fact]
    public async Task Load_more_appends_until_the_total_is_reached()
    {
        var query = new FakeLibraryQuery(total: 150, creators: []); // 2 pages of 100
        var vm = new LibraryViewModel(query);

        await vm.RefreshAsync();
        Assert.Equal(100, vm.Items.Count);
        Assert.True(vm.HasMore);

        await vm.LoadMoreAsync();
        Assert.Equal(150, vm.Items.Count);
        Assert.False(vm.HasMore);
    }

    private sealed class FakeLibraryQuery(int total, IReadOnlyList<string> creators) : ILibraryQueryService
    {
        public LibraryQuery? LastQuery { get; private set; }

        public Task<LibraryPage> GetPageAsync(LibraryQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            var remaining = Math.Max(0, total - query.Skip);
            var count = Math.Min(remaining, query.Take);
            var items = Enumerable.Range(query.Skip, count)
                .Select(i => new PackageListEntry(i, $"C.P.{i}", "C", "P", "1", "Scene", 1, 1, 1, true, false, "Cold", false, null))
                .ToList();
            return Task.FromResult(new LibraryPage(items, total));
        }

        public Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(creators);
    }
}
