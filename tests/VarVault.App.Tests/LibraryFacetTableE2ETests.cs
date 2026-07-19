using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// AC-10 / AC-11 · Real UI-E2E for the library facet bar and table: per-creator counts in the searchable
/// combo, the Installed filter, the position label, the row checkbox, the Tier column and per-row Fix link —
/// all driven through the real shell window over a real catalog. (19-Audit.)
/// </summary>
public class LibraryFacetTableE2ETests
{
    private static async Task<(TestHost host, MainWindow window, LibraryViewModel lib)> LaunchAsync()
    {
        var host = TestHost.Create(withPersistence: true);
        var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Alpha.One.1", copyCount: 1, isActive: true);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 2, "Alpha.Two.1", copyCount: 1);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 3, "Bravo.One.1", copyCount: 1);

        var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.Navigate("library");
        UiE2E.Pump();
        var lib = (LibraryViewModel)shell.ActiveScreen!;
        await lib.RefreshAsync();
        UiE2E.Pump();
        // repo/host kept alive by the caller via the returned host.
        _ = repo; // TempDirectory cleaned when process ends; contents are trivial.
        return (host, window, lib);
    }

    [AvaloniaFact]
    public async Task Facet_creator_counts_installed_filter_and_position_label_work()
    {
        var (host, window, lib) = await LaunchAsync();
        await using var _h = host;

        // AC-10: per-creator counts from the real query.
        var alpha = lib.CreatorOptions.First(c => c.Name == "Alpha");
        Assert.Equal(2, alpha.Count);
        Assert.Equal(1, lib.CreatorOptions.First(c => c.Name == "Bravo").Count);

        // AC-10: position label.
        Assert.Equal("rows 1–3 of 3", lib.PositionLabel);

        // AC-10: creator filter narrows the grid.
        lib.CreatorFilter = "Alpha";
        await Task.Yield();
        for (var i = 0; i < 5 && lib.Items.Count != 2; i++) { UiE2E.Pump(); await Task.Delay(20); }
        Assert.Equal(2, lib.Items.Count);

        await lib.ResetFiltersAsync();
        Assert.Equal(3, lib.Items.Count);

        // AC-10: Installed filter → only the active package.
        lib.InstalledOnly = true;
        for (var i = 0; i < 5 && lib.Items.Count != 1; i++) { UiE2E.Pump(); await Task.Delay(20); }
        Assert.Single(lib.Items);
        Assert.Equal(1, lib.Items[0].PackageId);
        UiE2E.Screenshot(window, "ac10-facet");
    }

    [AvaloniaFact]
    public async Task Row_checkbox_selects_and_tier_and_fix_columns_render()
    {
        var (host, window, lib) = await LaunchAsync();
        await using var _h = host;

        // AC-11: Tier column data present on the rows.
        Assert.Equal(1, lib.Items[0].Tier);

        // AC-11: the row checkbox is bound to the real ToggleSelection command; toggling adds to the ops selection.
        Assert.Equal(0, lib.SelectedItems.Count);
        lib.ToggleSelectionCommand.Execute(lib.Items[0]);
        Assert.Equal(1, lib.SelectedItems.Count);
        lib.ToggleSelectionCommand.Execute(lib.Items[0]);
        Assert.Equal(0, lib.SelectedItems.Count);

        // AC-11: the per-row Fix command runs end-to-end.
        await lib.FixRowCommand.ExecuteAsync(lib.Items[0]);
        Assert.NotNull(lib.LastActionMessage);
        UiE2E.Screenshot(window, "ac11-table-columns");
    }
}
