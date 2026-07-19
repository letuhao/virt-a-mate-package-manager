using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Sdk.Library;

namespace VarVault.App.Tests;

/// <summary>
/// UI-E2E under Avalonia's headless dispatcher (TO-10): the shell window binds the library view-model
/// and renders its rows once loaded.
/// </summary>
public class MainWindowUiTests
{
    [AvaloniaFact]
    public async Task Main_window_shows_library_rows_after_refresh()
    {
        var query = new StubLibrary(count: 4);
        var vm = new MainWindowViewModel(new LibraryViewModel(query));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("VarVault", window.Title);

        await vm.Library.RefreshAsync();
        Dispatcher.UIThread.RunJobs();

        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.Equal(4, list.ItemCount);
        Assert.Equal(4, vm.Library.TotalCount);
    }

    private sealed class StubLibrary(int count) : ILibraryQueryService
    {
        public Task<LibraryPage> GetPageAsync(LibraryQuery query, CancellationToken cancellationToken = default)
        {
            var items = Enumerable.Range(0, count)
                .Select(i => new PackageListEntry(i, $"C.P.{i}", "C", "P", "1", "Scene", 1, 1, 1, true, false, "Cold", false, null))
                .ToList();
            return Task.FromResult(new LibraryPage(items, count));
        }

        public Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["C"]);

        public Task<IReadOnlyList<long>> GetOrderedIdsAsync(LibraryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<long>>(Enumerable.Range(0, count).Select(i => (long)i).ToList());
    }
}
