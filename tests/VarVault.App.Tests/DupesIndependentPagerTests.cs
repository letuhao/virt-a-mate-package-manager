using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Exact and Near duplicate pagers keep independent page/size state.</summary>
[Trait("Category", TestCategories.Unit)]
public sealed class DupesIndependentPagerTests
{
    private sealed class StubReclaim : IReclaimService
    {
        public Task<PageResult<DuplicateGroup>> ExactGroupsPageAsync(PageRequest request, CancellationToken cancellationToken = default)
        {
            var page = request.Normalize();
            var all = Enumerable.Range(1, 80)
                .Select(i => new DuplicateGroup($"id{i}", $"sig{i}", [
                    new DuplicateCopy(i * 10, 1, $@"D:\a{i}.var", true, 100),
                    new DuplicateCopy(i * 10 + 1, 2, $@"D:\b{i}.var", true, 100),
                ]))
                .ToList();
            return Task.FromResult(new PageResult<DuplicateGroup>(
                all.Skip(page.Skip).Take(page.SafePageSize).ToList(),
                all.Count, page.SafePageNumber, page.SafePageSize));
        }

        public Task<PageResult<NearDuplicateGroup>> NearDuplicateGroupsPageAsync(PageRequest request, CancellationToken cancellationToken = default)
        {
            var page = request.Normalize();
            var all = Enumerable.Range(1, 30)
                .Select(i => new NearDuplicateGroup($"pay{i}", $"A.Near{i}.1", [
                    new DuplicateCopy(i, 1, $@"D:\n{i}.var", true, 50),
                ]))
                .ToList();
            return Task.FromResult(new PageResult<NearDuplicateGroup>(
                all.Skip(page.Skip).Take(page.SafePageSize).ToList(),
                all.Count, page.SafePageNumber, page.SafePageSize));
        }

        public Task<IReadOnlyList<DuplicateGroup>> ExactGroupsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DuplicateGroup>>([]);
        public Task<IReadOnlyList<NearDuplicateGroup>> NearDuplicateGroupsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NearDuplicateGroup>>([]);
        public Task<ReclaimResult> TrashRedundantAsync(long keepVarFileId, IReadOnlyList<long> trashVarFileIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReclaimResult(0, 0));
    }

    [Fact]
    public async Task Exact_and_near_pagers_navigate_independently()
    {
        var vm = new DupesViewModel(new StubReclaim());
        await vm.LoadAsync();
        Assert.Equal(1, vm.ExactPager.PageNumber);
        Assert.Equal(1, vm.NearPager.PageNumber);
        Assert.Equal(50, vm.ExactPager.Items.Count);
        Assert.Equal(30, vm.NearPager.Items.Count);

        await vm.ExactNextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.ExactPager.PageNumber);
        Assert.Equal(1, vm.NearPager.PageNumber); // untouched

        await vm.NearChangePageSizeCommand.ExecuteAsync(25);
        Assert.Equal(2, vm.ExactPager.PageNumber); // untouched
        Assert.Equal(1, vm.NearPager.PageNumber);
        Assert.Equal(25, vm.NearPager.PageSize);
    }
}
