using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
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

        var list = window.GetVisualDescendants().OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, vm.Library.Items) && l.IsVisible);
        Assert.Equal(4, list.ItemCount);
        Assert.Equal(4, vm.Library.TotalCount);
    }

    [AvaloniaFact]
    public async Task Empty_state_renders_when_no_rows()
    {
        var vm = new MainWindowViewModel(new LibraryViewModel(new StubLibrary(count: 0)));
        var window = new MainWindow { DataContext = vm };
        window.Show();

        await vm.Library.RefreshAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.Library.IsEmpty);
        var emptyText = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "No packages match your filters.");
        Assert.NotNull(emptyText);
        Assert.True(emptyText!.IsVisible);
    }

    [AvaloniaFact]
    public async Task Selecting_a_row_populates_the_detail_panel()
    {
        var vm = new MainWindowViewModel(new LibraryViewModel(new StubLibrary(count: 3)));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        await vm.Library.RefreshAsync();
        Dispatcher.UIThread.RunJobs();

        var list = window.GetVisualDescendants().OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, vm.Library.Items) && l.IsVisible);
        list.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(vm.Library.SelectedEntry); // detail panel binds to this
        Assert.Equal(vm.Library.Items[1], vm.Library.SelectedEntry);
    }

    [AvaloniaFact]
    public void Window_renders_in_both_theme_variants()
    {
        var vm = new MainWindowViewModel(new LibraryViewModel(new StubLibrary(count: 2)));

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var light = new MainWindow { DataContext = vm };
        light.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeVariant.Light, light.ActualThemeVariant);

        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        var dark = new MainWindow { DataContext = vm };
        dark.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeVariant.Dark, dark.ActualThemeVariant);
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
