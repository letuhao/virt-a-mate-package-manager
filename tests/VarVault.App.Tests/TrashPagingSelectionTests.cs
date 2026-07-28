using VarVault.App.ViewModels;
using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Trash selection is keyed by stable id and survives page navigation.</summary>
[Trait("Category", TestCategories.Unit)]
public sealed class TrashPagingSelectionTests
{
    private sealed class StubTrash : ITrashQueryService
    {
        private readonly List<TrashItemDto> _items;
        public List<string> RestoredIds { get; } = [];

        public StubTrash(int count = 120)
        {
            _items = Enumerable.Range(1, count)
                .Select(i => new TrashItemDto($"id{i}", $@"D:\item{i}.var", "superseded", DateTime.UtcNow, i))
                .ToList();
        }

        public Task<PageResult<TrashItemDto>> ListPageAsync(
            PageRequest request, string? searchText = null, CancellationToken cancellationToken = default)
        {
            var page = request.Normalize();
            return Task.FromResult(new PageResult<TrashItemDto>(
                _items.Skip(page.Skip).Take(page.SafePageSize).ToList(),
                _items.Count,
                page.SafePageNumber,
                page.SafePageSize));
        }

        public Task<IReadOnlyList<TrashItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TrashItemDto>>(_items.ToList());

        public Task<Result> RestoreAsync(string trashId, CancellationToken cancellationToken = default)
        {
            RestoredIds.Add(trashId);
            _items.RemoveAll(i => i.Id == trashId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> PurgeAsync(string trashId, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(i => i.Id == trashId);
            return Task.FromResult(Result.Success());
        }

        public Task<IReadOnlyList<BackupDto>> ListBackupsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BackupDto>>([]);
        public Task<Result<BackupDto>> BackupNowAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new BackupDto("b", DateTime.UtcNow, 1)));
    }

    [Fact]
    public async Task Cross_page_selection_preserves_count_and_resyncs_visible_checks()
    {
        var vm = new TrashViewModel(new StubTrash());
        await vm.LoadAsync();
        Assert.Equal(50, vm.Items.Count);

        var first = vm.Items[0];
        vm.ToggleSelection(first);
        Assert.Equal(1, vm.SelectedCount);
        Assert.True(first.IsSelected);
        Assert.True(vm.IsSelected(first));

        await vm.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Pager.PageNumber);
        Assert.Equal(1, vm.SelectedCount); // selection survives off-page
        Assert.Empty(vm.SelectedItems); // visible sync only shows current page hits

        var secondPageItem = vm.Items[0];
        vm.ToggleSelection(secondPageItem);
        Assert.Equal(2, vm.SelectedCount);
        Assert.True(secondPageItem.IsSelected);

        await vm.PreviousPageCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.Pager.PageNumber);
        Assert.Equal(2, vm.SelectedCount);
        Assert.Single(vm.SelectedItems);
        Assert.Equal(first.Id, vm.SelectedItems[0].Id);
        Assert.True(vm.Items[0].IsSelected);
    }

    [Fact]
    public async Task SelectPage_only_selects_visible_rows()
    {
        var vm = new TrashViewModel(new StubTrash());
        await vm.LoadAsync();
        Assert.Equal(50, vm.Items.Count);

        vm.SelectPage();
        Assert.Equal(50, vm.SelectedCount);
        Assert.All(vm.Items, row => Assert.True(row.IsSelected));
    }

    [Fact]
    public async Task SelectAll_selects_entire_trash_not_just_page()
    {
        var vm = new TrashViewModel(new StubTrash());
        await vm.LoadAsync();
        Assert.Equal(50, vm.Items.Count);
        Assert.Equal(120, vm.Pager.TotalCount);

        await vm.SelectAllCommand.ExecuteAsync(null);
        Assert.Equal(120, vm.SelectedCount);
        Assert.All(vm.Items, row => Assert.True(row.IsSelected));

        vm.ClearSelection();
        Assert.Equal(0, vm.SelectedCount);
        Assert.All(vm.Items, row => Assert.False(row.IsSelected));
    }

    [Fact]
    public async Task RestoreSelected_only_restores_selected_ids_not_entire_trash()
    {
        var stub = new StubTrash(120);
        var vm = new TrashViewModel(stub);
        await vm.LoadAsync();

        vm.SelectPage(); // 50 on page 1
        Assert.Equal(50, vm.SelectedCount);

        await vm.RestoreSelectedCommand.ExecuteAsync(null);

        Assert.Equal(50, stub.RestoredIds.Count);
        Assert.Equal(70, vm.Pager.TotalCount);
        Assert.DoesNotContain(stub.RestoredIds, id => id == "id51"); // page 2 not restored
    }

    [Fact]
    public async Task Restore_reloads_list_so_restored_item_disappears()
    {
        var stub = new StubTrash();
        var vm = new TrashViewModel(stub);
        await vm.LoadAsync();
        Assert.Equal(120, vm.Pager.TotalCount);

        var firstId = vm.Items[0].Id;
        await vm.RestoreCommand.ExecuteAsync(vm.Items[0]);

        Assert.Equal(119, vm.Pager.TotalCount);
        Assert.DoesNotContain(vm.Items, r => r.Id == firstId);
    }
}
