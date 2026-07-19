using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SCR-2 · Library screen renders rail/facet/table/detail and exports the selection. (16-checklist SCR-2.)</summary>
[Trait("Category", TestCategories.Unit)]
public class LibraryScreenTests
{
    private sealed class StubLibrary(int count) : ILibraryQueryService
    {
        public Task<LibraryPage> GetPageAsync(LibraryQuery query, CancellationToken ct = default)
        {
            var items = Enumerable.Range(0, count)
                .Select(i => new PackageListEntry(i, $"C.P{i}.1", "C", $"P{i}", "1", "Scene", 1024, 1, 1, true, false, "Cold", false, null))
                .ToList();
            return Task.FromResult(new LibraryPage(items, count));
        }
        public Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["C"]);
        public Task<IReadOnlyList<long>> GetOrderedIdsAsync(LibraryQuery q, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<long>>([]);
    }

    private sealed class StubActions : ILibraryActionService
    {
        public Task<BulkActionResult> AddToPresetAsync(long p, IReadOnlyList<long> ids, CancellationToken ct = default) => Task.FromResult(new BulkActionResult(ids.Count, 0));
        public Task<BulkActionResult> FixEncodingAsync(IReadOnlyList<long> ids, CancellationToken ct = default) => Task.FromResult(new BulkActionResult(0, 0));
        public Task<BulkActionResult> DeleteAsync(IReadOnlyList<long> ids, CancellationToken ct = default) => Task.FromResult(new BulkActionResult(0, 0));
        public Task<string> ExportTxtAsync(IReadOnlyList<long> ids, CancellationToken ct = default) => Task.FromResult(string.Join('\n', ids));
        public Task<BulkActionResult> MoveToSubfolderAsync(IReadOnlyList<long> v, string s, CancellationToken ct = default) => Task.FromResult(new BulkActionResult(v.Count, 0));
        public Task<TxtResolveResult> ResolveTxtAsync(string t, CancellationToken ct = default) => Task.FromResult(new TxtResolveResult([], []));
    }

    [AvaloniaFact]
    public async Task Renders_table_rows_and_detail()
    {
        var vm = new LibraryViewModel(new StubLibrary(5), actions: new StubActions());
        await vm.RefreshAsync();
        var view = new LibraryView { DataContext = vm };
        var window = new Window { Width = 1000, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var list = view.GetVisualDescendants().OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, vm.Items) && l.IsVisible);
        Assert.Equal(5, list.ItemCount);

        vm.SelectedEntry = vm.Items[0];
        Dispatcher.UIThread.RunJobs();
        var detail = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);
        Assert.Contains("C.P0.1", detail); // detail panel bound to selection
    }

    [AvaloniaFact]
    public async Task Export_selection_produces_txt()
    {
        var vm = new LibraryViewModel(new StubLibrary(3), actions: new StubActions());
        await vm.RefreshAsync();
        vm.SelectVisible();

        await vm.ExportSelectedCommand.ExecuteAsync(null);

        Assert.NotNull(vm.LastExportText);
        Assert.Contains("Exported 3", vm.LastActionMessage);
    }

    [AvaloniaFact]
    public async Task Rail_favorites_filters()
    {
        var vm = new LibraryViewModel(new StubLibrary(2), actions: new StubActions());
        await vm.ShowFavoritesCommand.ExecuteAsync(null);
        Assert.True(vm.FavoritesOnly);
    }
}
