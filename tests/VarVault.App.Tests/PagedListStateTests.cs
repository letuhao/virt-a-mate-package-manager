using VarVault.App.ViewModels;
using VarVault.Sdk.Paging;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>PagedListState: navigation, page-size reset, clamping, cancellation, and error/empty states.</summary>
[Trait("Category", TestCategories.Unit)]
public sealed class PagedListStateTests
{
    [Fact]
    public async Task Navigates_first_middle_last_and_reports_totals()
    {
        var state = new PagedListState<int>((request, _) =>
        {
            var all = Enumerable.Range(1, 120).ToList();
            var page = request.Normalize();
            return Task.FromResult(new PageResult<int>(
                all.Skip(page.Skip).Take(page.SafePageSize).ToList(),
                all.Count,
                page.SafePageNumber,
                page.SafePageSize));
        }, defaultPageSize: 50);

        await state.ResetAndReloadAsync();
        Assert.Equal(50, state.Items.Count);
        Assert.Equal(120, state.TotalCount);
        Assert.Equal(1, state.PageNumber);
        Assert.Equal("1-50 of 120", state.SummaryLabel);
        Assert.False(state.HasPreviousPage);
        Assert.True(state.HasNextPage);

        await state.NextPageAsync();
        Assert.Equal(2, state.PageNumber);
        Assert.Equal(51, state.Items[0]);
        Assert.Equal("51-100 of 120", state.SummaryLabel);

        await state.NextPageAsync();
        Assert.Equal(3, state.PageNumber);
        Assert.Equal(20, state.Items.Count);
        Assert.Equal("101-120 of 120", state.SummaryLabel);
        Assert.False(state.HasNextPage);

        await state.PreviousPageAsync();
        Assert.Equal(2, state.PageNumber);
    }

    [Fact]
    public async Task Page_size_change_resets_to_page_one()
    {
        var state = new PagedListState<int>((request, _) =>
        {
            var page = request.Normalize();
            return Task.FromResult(new PageResult<int>(
                Enumerable.Range(page.Skip + 1, Math.Min(page.SafePageSize, 80 - page.Skip)).ToList(),
                80,
                page.SafePageNumber,
                page.SafePageSize));
        });

        await state.LoadPageAsync(2, 50);
        Assert.Equal(2, state.PageNumber);
        await state.LoadPageAsync(1, 25);
        Assert.Equal(1, state.PageNumber);
        Assert.Equal(25, state.PageSize);
        Assert.Equal(25, state.Items.Count);
    }

    [Fact]
    public async Task Out_of_range_page_clamps_to_last_valid_page()
    {
        var state = new PagedListState<int>((request, _) =>
        {
            var page = request.Normalize();
            return Task.FromResult(new PageResult<int>(
                [],
                TotalCount: 30,
                page.SafePageNumber,
                page.SafePageSize));
        }, defaultPageSize: 50);

        await state.LoadPageAsync(9, 50);
        Assert.Equal(1, state.PageNumber);
        Assert.Equal(30, state.TotalCount);
    }

    [Fact]
    public async Task Stale_load_is_cancelled_by_a_newer_request()
    {
        var tcs1 = new TaskCompletionSource<PageResult<int>>();
        var calls = 0;
        var state = new PagedListState<int>(async (request, ct) =>
        {
            var n = Interlocked.Increment(ref calls);
            if (n == 1)
            {
                await using (ct.Register(() => tcs1.TrySetCanceled(ct)))
                    return await tcs1.Task.ConfigureAwait(false);
            }
            return new PageResult<int>([99], 1, request.SafePageNumber, request.SafePageSize);
        });

        var first = state.LoadPageAsync(1, 50);
        await state.LoadPageAsync(1, 25);
        Assert.Equal([99], state.Items.ToArray());
        Assert.False(state.IsLoading);
        // Completing the cancelled first load must not overwrite the newer page.
        tcs1.TrySetResult(new PageResult<int>([1], 1, 1, 50));
        try { await first; } catch (OperationCanceledException) { /* expected when linked token cancels */ }
        Assert.Equal([99], state.Items.ToArray());
    }

    [Fact]
    public async Task Empty_and_error_states_surface()
    {
        var empty = new PagedListState<int>((request, _) =>
            Task.FromResult(PageResult<int>.Empty(request)));
        await empty.ResetAndReloadAsync();
        Assert.True(empty.IsEmpty);
        Assert.Equal("0 results", empty.SummaryLabel);

        var failing = new PagedListState<int>((_, _) => throw new InvalidOperationException("boom"));
        await failing.ResetAndReloadAsync();
        Assert.Equal("boom", failing.ErrorMessage);
        Assert.False(failing.IsEmpty); // error is not the empty-success state
        Assert.Empty(failing.Items);
    }
}
