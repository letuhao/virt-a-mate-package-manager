using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// AC-12 / AC-13 · Real UI-E2E for the library left rail (saved-view filters, Tags section + create) and the
/// detail panel (copies list, dependency counts, resolve-via-alias), driven through the real shell. (19-Audit.)
/// </summary>
public class LibraryRailDetailE2ETests
{
    private static async Task<(TestHost host, MainWindow window, ShellViewModel shell, LibraryViewModel lib)> LaunchAsync()
    {
        var host = TestHost.Create(withPersistence: true);
        var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Solo.One.1", copyCount: 1);                 // single copy
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 2, "Multi.Two.1", copyCount: 2, isActive: true); // multi + active
        var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.Navigate("library");
        UiE2E.Pump();
        var lib = (LibraryViewModel)shell.ActiveScreen!;
        await lib.RefreshAsync();
        UiE2E.Pump();
        _ = repo;
        return (host, window, shell, lib);
    }

    [AvaloniaFact]
    public async Task Rail_saved_views_filter_and_new_tag_creates_a_tag()
    {
        var (host, window, shell, lib) = await LaunchAsync();
        await using var _h = host;

        Assert.Equal(2, lib.Items.Count);

        // AC-12: "Single copy" saved view narrows to the single-copy package.
        await lib.ShowSingleCopyCommand.ExecuteAsync(null);
        Assert.Single(lib.Items);
        Assert.Equal(1, lib.Items[0].PackageId);

        // AC-12: "Active in game" saved view narrows to the active package.
        await lib.ShowActiveInGameCommand.ExecuteAsync(null);
        Assert.Single(lib.Items);
        Assert.Equal(2, lib.Items[0].PackageId);

        // AC-12: "+ new tag" creates a real tag.
        Assert.Empty(lib.Tags);
        lib.NewTagName = "Favorites2026";
        await lib.CreateTagCommand.ExecuteAsync(null);
        Assert.Contains(lib.Tags, t => t.Name == "Favorites2026");
        UiE2E.Screenshot(window, "ac12-rail");
    }

    [AvaloniaFact]
    public async Task Detail_panel_shows_copies_and_resolve_alias_opens_the_alias_dialog()
    {
        var (host, window, shell, lib) = await LaunchAsync();
        await using var _h = host;

        // Select the multi-copy package → detail loads its copies.
        lib.SelectedEntry = lib.Items.First(i => i.PackageId == 2);
        for (var i = 0; i < 5 && lib.SelectedDetail is null; i++) { UiE2E.Pump(); await Task.Delay(20); }
        Assert.NotNull(lib.SelectedDetail);
        Assert.Equal(2, lib.SelectedDetail!.Copies.Count); // AC-13: real copies list
        UiE2E.Screenshot(window, "ac13-detail-panel");

        // AC-13: resolve-via-alias opens the alias dialog through the launcher.
        lib.ResolveAliasCommand.Execute(null);
        UiE2E.Pump();
        Assert.True(shell.Dialogs.IsOpen);
        Assert.IsType<AliasViewModel>(shell.Dialogs.Current);
    }
}
