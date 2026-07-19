using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// AC-7 / AC-8 · Real UI-E2E for shell log-dock selection count and top-bar global search, driving the
/// actual shell window over a seeded library. (19-Audit.)
/// </summary>
public class ShellWiringE2ETests
{
    private static async Task<(TestHost host, MainWindow window, ShellViewModel shell, LibraryViewModel lib, TempDirectory repo)>
        LaunchAsync()
    {
        var host = TestHost.Create(withPersistence: true);
        var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Alpha.One.1", copyCount: 1);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 2, "Bravo.Two.1", copyCount: 1);

        var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.Navigate("library");
        UiE2E.Pump();
        var lib = (LibraryViewModel)shell.ActiveScreen!;
        await lib.RefreshAsync();
        UiE2E.Pump();
        return (host, window, shell, lib, repo);
    }

    [AvaloniaFact]
    public async Task Log_dock_selected_count_tracks_the_library_selection()
    {
        var (host, window, shell, lib, repo) = await LaunchAsync();
        await using var _h = host;
        using var _r = repo;

        Assert.Equal(0, shell.SelectedCount);

        lib.SelectedItems.Add(lib.Items[0]);
        UiE2E.Pump();
        Assert.Equal(1, shell.SelectedCount); // AC-7: was permanently 0 before

        lib.SelectedItems.Add(lib.Items[1]);
        UiE2E.Pump();
        Assert.Equal(2, shell.SelectedCount);
        UiE2E.Screenshot(window, "ac7-selected-count");

        // Navigating away clears the count.
        shell.Navigate("dashboard");
        UiE2E.Pump();
        Assert.Equal(0, shell.SelectedCount);
    }

    [AvaloniaFact]
    public async Task Top_bar_search_enter_filters_the_library()
    {
        var (host, window, shell, lib, repo) = await LaunchAsync();
        await using var _h = host;
        using var _r = repo;

        Assert.Equal(2, lib.Items.Count);

        // Type in the real top-bar search and press Enter (OpenPaletteCommand).
        shell.SearchText = "Alpha";
        shell.OpenPaletteCommand.Execute(null);
        if (lib.PendingRefresh is not null)
            await lib.PendingRefresh;
        UiE2E.Pump();

        // AC-8: the search now has a visible effect — it navigated to the library and filtered it.
        Assert.Equal("library", shell.ActiveScreenId);
        Assert.Equal("Alpha", lib.SearchText);
        Assert.Single(lib.Items);
        Assert.Equal(1, lib.Items[0].PackageId);
        UiE2E.Screenshot(window, "ac8-search-filtered");
    }
}
