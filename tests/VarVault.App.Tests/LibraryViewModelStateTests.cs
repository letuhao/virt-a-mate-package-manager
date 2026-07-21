using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.Sdk.Settings;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// Library view-model logic (sort toggle, view-mode, selection, states, remembered view) as plain
/// units. (Checklist 1.44/1.45/1.49/1.50/1.52.)
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class LibraryViewModelStateTests
{
    [Fact]
    public async Task Sort_by_toggles_direction_on_the_same_column()
    {
        var query = new StubLibrary(total: 2);
        var vm = new LibraryViewModel(query);

        await vm.SortByAsync(LibrarySort.Size);
        Assert.Equal(LibrarySort.Size, vm.Sort);
        Assert.False(vm.Descending);

        await vm.SortByAsync(LibrarySort.Size); // same column → flip direction
        Assert.True(vm.Descending);

        await vm.SortByAsync(LibrarySort.Creator); // new column → reset ascending
        Assert.Equal(LibrarySort.Creator, vm.Sort);
        Assert.False(vm.Descending);
    }

    [Fact]
    public async Task View_mode_toggles()
    {
        var vm = new LibraryViewModel(new StubLibrary(0));
        Assert.Equal(LibraryViewMode.Table, vm.ViewMode);
        await vm.ToggleViewModeAsync();
        Assert.Equal(LibraryViewMode.Gallery, vm.ViewMode);
    }

    [Fact]
    public async Task Empty_and_loaded_states_are_set()
    {
        var empty = new LibraryViewModel(new StubLibrary(0));
        await empty.RefreshAsync();
        Assert.True(empty.IsEmpty);
        Assert.False(empty.IsLoading);

        var loaded = new LibraryViewModel(new StubLibrary(5));
        await loaded.RefreshAsync();
        Assert.Equal(LibraryState.Loaded, loaded.State);
    }

    [Fact]
    public async Task Error_state_when_the_query_throws()
    {
        var vm = new LibraryViewModel(new ThrowingLibrary());
        await vm.RefreshAsync();
        Assert.True(vm.HasError);
    }

    [Fact]
    public async Task Select_visible_selects_loaded_rows()
    {
        var vm = new LibraryViewModel(new StubLibrary(3));
        await vm.RefreshAsync();
        vm.SelectVisible();
        Assert.Equal(3, vm.SelectedItems.Count);
        vm.ClearSelection();
        Assert.Empty(vm.SelectedItems);
    }

    [Fact]
    public async Task Count_all_matching_spans_beyond_the_loaded_page()
    {
        var vm = new LibraryViewModel(new StubLibrary(250)); // more than one page
        await vm.RefreshAsync();
        Assert.Equal(100, vm.Items.Count);              // only a page loaded
        Assert.Equal(250, await vm.CountAllMatchingAsync()); // but all-matching is 250
    }

    [Fact]
    public async Task Remembered_view_round_trips_through_settings()
    {
        var settings = new FakeSettings();
        var first = new LibraryViewModel(new StubLibrary(1), settings);
        await first.SortByAsync(LibrarySort.LastUsed);
        await first.ToggleViewModeAsync();

        // A fresh view-model restores the remembered sort + view-mode.
        var second = new LibraryViewModel(new StubLibrary(1), settings);
        await second.LoadPreferencesAsync();
        Assert.Equal(LibrarySort.LastUsed, second.Sort);
        Assert.Equal(LibraryViewMode.Gallery, second.ViewMode);
    }

    [Fact]
    public async Task Select_all_matching_covers_unordered_snapshot_beyond_loaded_page()
    {
        var vm = new LibraryViewModel(new StubLibrary(250));
        await vm.RefreshAsync();
        Assert.Equal(100, vm.Items.Count);

        vm.SelectAllMatching();

        Assert.Equal(250, vm.SelectedCount);
        Assert.Equal(100, vm.SelectedItems.Count); // projection is loaded rows only
        Assert.Equal(250, await vm.CountAllMatchingAsync());
    }

    [Fact]
    public async Task Unchecking_one_loaded_row_after_select_all_drops_only_that_id()
    {
        var vm = new LibraryViewModel(new StubLibrary(150));
        await vm.RefreshAsync();
        vm.SelectAllMatching();
        Assert.Equal(150, vm.SelectedCount);

        vm.SetSelected(vm.Items[0].PackageId, false);

        Assert.Equal(149, vm.SelectedCount);
        Assert.False(vm.IsSelected(vm.Items[0]));
    }

    [Fact]
    public async Task Table_gallery_focus_survives_view_mode_toggle()
    {
        var vm = new LibraryViewModel(new StubLibrary(5));
        await vm.RefreshAsync();
        vm.SelectEntry(vm.Items[2]);
        Assert.Equal(2, vm.SelectedEntry!.PackageId);

        await vm.ToggleViewModeAsync();
        Assert.Equal(LibraryViewMode.Gallery, vm.ViewMode);
        Assert.Equal(2, vm.SelectedEntry!.PackageId);
        Assert.True(vm.GalleryItems.Single(c => c.PackageId == 2).IsSelected);
    }

    [Fact]
    public async Task Empty_preferences_keep_added_descending_default()
    {
        var vm = new LibraryViewModel(new StubLibrary(0), new FakeSettings());
        await vm.LoadPreferencesAsync();
        Assert.Equal(LibrarySort.Added, vm.Sort);
        Assert.True(vm.Descending);
    }

    [Fact]
    public async Task Refresh_clears_stale_focus_and_detail_selection()
    {
        var vm = new LibraryViewModel(new StubLibrary(2));
        await vm.RefreshAsync();
        vm.SelectEntry(vm.Items[0]);
        vm.SelectAllMatching();
        Assert.NotNull(vm.SelectedEntry);
        Assert.Equal(2, vm.SelectedCount);

        await vm.RefreshAsync();

        Assert.Null(vm.SelectedEntry);
        Assert.Null(vm.SelectedDetail);
        Assert.Equal(0, vm.SelectedCount);
        Assert.Empty(vm.SelectedItems);
    }

    [Fact]
    public async Task Column_layout_round_trips_as_a_versioned_setting()
    {
        var settings = new FakeSettings();
        var vm = new LibraryViewModel(new StubLibrary(0), settings);
        await vm.SaveColumnLayoutAsync("[{\"Id\":\"Name\"}]");
        Assert.Equal("[{\"Id\":\"Name\"}]", await vm.LoadColumnLayoutAsync());
    }

    [Fact]
    public async Task Ensure_index_loaded_appends_until_target_is_covered()
    {
        var vm = new LibraryViewModel(new StubLibrary(250));
        await vm.RefreshAsync();
        Assert.Equal(100, vm.Items.Count);

        await vm.EnsureIndexLoadedAsync(150);

        Assert.True(vm.Items.Count > 150);
        Assert.Contains(vm.Items, i => i.PackageId == 150);
    }

    [Fact]
    public async Task Typing_flags_count_approximate_then_exact_after_settle()
    {
        // A gate delay holds the debounced refresh open so the interim (approximate) state is observed
        // deterministically — a yielding delay would let the pooled refresh clear the flag before the
        // assert under load (a race).
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new LibraryViewModel(new StubLibrary(7), delay: (_, ct) =>
        {
            ct.Register(() => gate.TrySetCanceled());
            return gate.Task;
        });

        vm.SearchText = "sce";
        Assert.True(vm.IsCountApproximate);          // in-flight (delay still pending): count is approximate

        gate.SetResult();                            // let the debounce settle → refresh runs
        await vm.PendingRefresh!;
        Assert.False(vm.IsCountApproximate);          // now exact
        Assert.Equal(7, vm.TotalCount);
    }

    [Fact]
    public async Task Rapid_typing_coalesces_into_a_single_refresh()
    {
        // A gate delay per keystroke: superseded keystrokes are cancelled; only the last one fires.
        var gates = new List<TaskCompletionSource>();
        var library = new CountingLibrary(total: 3);
        var vm = new LibraryViewModel(library, delay: (_, ct) =>
        {
            var tcs = new TaskCompletionSource();
            gates.Add(tcs);
            ct.Register(() => tcs.TrySetCanceled());
            return tcs.Task;
        });

        vm.SearchText = "s";    // gate[0]
        vm.SearchText = "sc";   // cancels gate[0], gate[1]
        vm.SearchText = "sce";  // cancels gate[1], gate[2] pends

        Assert.Equal(0, library.PageCalls);           // nothing queried mid-typing
        gates[^1].TrySetResult();                     // the settled keystroke proceeds
        await vm.PendingRefresh!;

        Assert.Equal(1, library.PageCalls);           // exactly one refresh, not three
        Assert.False(vm.IsCountApproximate);
    }

    [Fact]
    public void Format_size_is_human_readable()
    {
        // Now delegates to the shared VarVault.Common.Formatting.ByteSize (28-checklist A1.4): TB=2dp, GB=1dp, KB/MB=0dp.
        Assert.Equal("1.0 GB", LibraryViewModel.FormatSize(1L << 30));
        Assert.Equal("2 MB", LibraryViewModel.FormatSize(2L << 20));
    }

    private sealed class StubLibrary(int total) : ILibraryQueryService
    {
        public Task<LibraryPage> GetPageAsync(LibraryQuery query, CancellationToken cancellationToken = default)
        {
            var remaining = Math.Max(0, total - query.Skip);
            var count = Math.Min(remaining, query.Take);
            var items = Enumerable.Range(query.Skip, count)
                .Select(i => new PackageListEntry(i, $"C.P.{i}", "C", "P", "1", "Scene", 1024, 1, 1, true, false, "Cold", false, null))
                .ToList();
            return Task.FromResult(new LibraryPage(items, total));
        }

        public Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["C"]);

        public Task<IReadOnlyList<long>> GetOrderedIdsAsync(LibraryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<long>>(Enumerable.Range(0, total).Select(i => (long)i).ToList());

        public Task<IReadOnlyList<PackageListEntry>> GetByIdsAsync(IReadOnlyList<long> packageIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PackageListEntry>>(
                packageIds.Select(i => new PackageListEntry(i, $"C.P.{i}", "C", "P", "1", "Scene", 1024, 1, 1, true, false, "Cold", false, null)).ToList());
    }

    private sealed class CountingLibrary(int total) : ILibraryQueryService
    {
        public int PageCalls { get; private set; }
        public int ByIdsCalls { get; private set; }

        public Task<LibraryPage> GetPageAsync(LibraryQuery query, CancellationToken cancellationToken = default)
        {
            PageCalls++;
            return Task.FromResult(new LibraryPage([], total));
        }

        public Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["C"]);
        public Task<IReadOnlyList<long>> GetOrderedIdsAsync(LibraryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<long>>([]);
        public Task<IReadOnlyList<PackageListEntry>> GetByIdsAsync(IReadOnlyList<long> packageIds, CancellationToken cancellationToken = default)
        {
            ByIdsCalls++;
            return Task.FromResult<IReadOnlyList<PackageListEntry>>([]);
        }
    }

    private sealed class ThrowingLibrary : ILibraryQueryService
    {
        public Task<LibraryPage> GetPageAsync(LibraryQuery query, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
        public Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<long>> GetOrderedIdsAsync(LibraryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<long>>([]);
        public Task<IReadOnlyList<PackageListEntry>> GetByIdsAsync(IReadOnlyList<long> packageIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PackageListEntry>>([]);
    }

    private sealed class FakeSettings : ISettingsService
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken ct = default) { _values[key] = value; return Task.CompletedTask; }
        public Task<bool> GetBoolAsync(string key, bool fallback = false, CancellationToken ct = default) =>
            Task.FromResult(_values.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : fallback);
        public Task SetBoolAsync(string key, bool value, CancellationToken ct = default) => SetAsync(key, value.ToString(), ct);
        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(_values);
    }
}
