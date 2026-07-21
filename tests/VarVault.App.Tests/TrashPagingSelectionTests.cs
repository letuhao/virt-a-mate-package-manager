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
        private readonly List<TrashItemDto> _items =
            Enumerable.Range(1, 120)
                .Select(i => new TrashItemDto($"id{i}", $@"D:\item{i}.var", "superseded", DateTime.UtcNow, i))
                .ToList();

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
            Task.FromResult<IReadOnlyList<TrashItemDto>>(_items);

        public Task<Result> RestoreAsync(string trashId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
        public Task<Result> PurgeAsync(string trashId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
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
        Assert.True(vm.IsSelected(first));

        await vm.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Pager.PageNumber);
        Assert.Equal(1, vm.SelectedCount); // selection survives off-page
        Assert.Empty(vm.SelectedItems); // visible sync only shows current page hits

        var secondPageItem = vm.Items[0];
        vm.ToggleSelection(secondPageItem);
        Assert.Equal(2, vm.SelectedCount);

        await vm.PreviousPageCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.Pager.PageNumber);
        Assert.Equal(2, vm.SelectedCount);
        Assert.Single(vm.SelectedItems);
        Assert.Equal(first.Id, vm.SelectedItems[0].Id);
    }
}
