using System.IO;
using Avalonia.Headless.XUnit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Infrastructure.Persistence;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// AC-9 · Real UI-E2E for the newly-added ops-bar operations: "Move to subfolder…" physically relocates the
/// selection and updates the catalog, and "select all N matching" selects the loaded rows. (19-Audit.)
/// </summary>
public class LibraryOpsMoveE2ETests
{
    [AvaloniaFact]
    public async Task Move_to_subfolder_button_relocates_the_file_and_updates_the_catalog()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        var pkg = await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Mover.Pack.1", copyCount: 1);
        var originalPath = pkg.Paths[0];
        Assert.True(File.Exists(originalPath));

        var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.Navigate("library");
        UiE2E.Pump();
        var lib = (LibraryViewModel)shell.ActiveScreen!;
        await lib.RefreshAsync();
        UiE2E.Pump();

        // "select all N matching" selects the loaded row.
        var selectAll = UiE2E.Button(window, "Select loaded");
        Assert.NotNull(selectAll);
        await UiE2E.ClickAsync(selectAll!);
        Assert.Equal(1, lib.SelectedItems.Count);

        // Type a sub-folder and click the real "Move to subfolder…" button.
        lib.SubfolderName = "Archive";
        UiE2E.Pump();
        var moveButton = UiE2E.Button(window, "Move to subfolder");
        Assert.NotNull(moveButton);
        Assert.NotNull(moveButton!.Command);
        await UiE2E.ClickAsync(moveButton);

        // Real effect: the file physically moved into the sub-folder and the catalog path updated.
        Assert.False(File.Exists(originalPath));
        var moved = Directory.GetFiles(Path.Combine(repo.Path, "Archive"));
        Assert.Single(moved);

        using var verify = host.Host.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var rel = await db.VarFiles.Where(v => v.Id == pkg.VarFileIds[0]).Select(v => v.RelativePath).FirstAsync();
        Assert.StartsWith("Archive", rel);
        UiE2E.Screenshot(window, "ac9-moved-to-subfolder");
    }
}
