using VarVault.App.ViewModels;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Paging;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Activity pager + kind filter: "All" uses SQL pages; filtered kinds page a capped window.</summary>
[Trait("Category", TestCategories.Unit)]
public sealed class ActivityPagingTests
{
    private sealed class StubLog : IActivityLog
    {
        private readonly List<ActivityRecord> _all =
            Enumerable.Range(0, 120)
                .Select(i => new ActivityRecord(
                    i % 3 == 0 ? "migrate" : i % 3 == 1 ? "delete" : "fix",
                    $"n={i}",
                    DateTime.UtcNow.AddMilliseconds(i)))
                .Reverse()
                .ToList();

        public Task RecordAsync(string kind, string description, long? packageId = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PageResult<ActivityRecord>> GetRecentPageAsync(PageRequest request, CancellationToken cancellationToken = default)
        {
            var page = request.Normalize();
            return Task.FromResult(new PageResult<ActivityRecord>(
                _all.Skip(page.Skip).Take(page.SafePageSize).ToList(),
                _all.Count,
                page.SafePageNumber,
                page.SafePageSize));
        }

        public Task<IReadOnlyList<ActivityRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityRecord>>(_all.Take(Math.Clamp(limit, 1, 1000)).ToList());
    }

    [Fact]
    public async Task All_actions_pages_with_exact_total()
    {
        var vm = new ActivityViewModel(new StubLog());
        await vm.RefreshAsync();
        Assert.Equal(50, vm.Items.Count);
        Assert.Equal(120, vm.Pager.TotalCount);
        Assert.Equal("n=119", vm.Items[0].Description);

        await vm.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Pager.PageNumber);
        Assert.Equal("n=69", vm.Items[0].Description);
    }

    [Fact]
    public async Task Kind_filter_resets_to_page_one_and_pages_filtered_subset()
    {
        var vm = new ActivityViewModel(new StubLog());
        await vm.RefreshAsync();
        await vm.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Pager.PageNumber);

        vm.SelectedFilter = "Fixes";
        // Filter change kicks RefreshAsync asynchronously — wait for the pager to settle.
        await WaitForAsync(() => vm.Pager.PageNumber == 1 && !vm.Pager.IsLoading && vm.Items.Count > 0);

        Assert.Equal(1, vm.Pager.PageNumber);
        Assert.All(vm.Items, i => Assert.Equal("fix", i.Kind));
        Assert.True(vm.Pager.TotalCount > 0);
        Assert.True(vm.Items.Count <= vm.Pager.PageSize);
    }

    private static async Task WaitForAsync(Func<bool> predicate, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (predicate()) return;
            await Task.Delay(20);
        }
        Assert.Fail("Timed out waiting for activity filter reload.");
    }
}
