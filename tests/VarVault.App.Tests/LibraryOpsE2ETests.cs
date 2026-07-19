using System.IO;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Sdk.Presets;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// AC-1..AC-4 · Real UI-E2E for the library ops-bar: launches the actual shell window over a real
/// SQLite catalog with real files on disk, then drives the real Delete / Add-to-preset / Fix-encoding
/// controls and asserts the real backend effects (files trashed, preset members added). (19-Audit.)
/// </summary>
public class LibraryOpsE2ETests
{
    private static async Task<(TestHost host, MainWindow window, ShellViewModel shell, LibraryViewModel lib,
        TempDirectory repo, UiE2ESeed.SeededPackage dup, UiE2ESeed.SeededPackage single)> LaunchWithLibraryAsync()
    {
        var host = TestHost.Create(withPersistence: true);
        var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        // A duplicate pair (2 verified online copies → one is safely deletable) with 2 reverse-dependents,
        // and a protected single copy.
        var dup = await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Dup.Pack.1", copyCount: 2, reverseDependents: 2);
        var single = await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 2, "Solo.Pack.1", copyCount: 1);

        var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.Navigate("library");
        UiE2E.Pump();

        var lib = (LibraryViewModel)shell.ActiveScreen!;
        await lib.RefreshAsync();
        UiE2E.Pump();
        return (host, window, shell, lib, repo, dup, single);
    }

    [AvaloniaFact]
    public async Task Delete_button_opens_confirm_with_reverse_dep_note_and_trashes_a_redundant_copy()
    {
        var (host, window, shell, lib, repo, dup, single) = await LaunchWithLibraryAsync();
        await using var _h = host;
        using var _r = repo;

        // Real data reached the real grid.
        Assert.Equal(2, lib.Items.Count);

        // Select both the redundant duplicate and the protected single copy, then click the real Delete button.
        lib.SelectedItems.Add(lib.Items.First(i => i.PackageId == dup.PackageId));
        lib.SelectedItems.Add(lib.Items.First(i => i.PackageId == single.PackageId));
        var deleteButton = UiE2E.Button(window, "Delete");
        Assert.NotNull(deleteButton);
        Assert.NotNull(deleteButton!.Command); // AC-1: the button is now wired (was a no-op)
        await UiE2E.ClickAsync(deleteButton);

        // AC-4: the confirm dialog opened through the dialog service, shows the reverse-dependency note,
        // and marks the single copy as protected.
        Assert.True(shell.Dialogs.IsOpen);
        var confirm = Assert.IsType<ConfirmDeleteViewModel>(shell.Dialogs.Current);
        Assert.True(confirm.HasReverseDeps);
        Assert.Equal(2, confirm.ReverseDepCount);
        Assert.Equal(1, confirm.ProtectedCount); // the single copy is excluded
        UiE2E.Screenshot(window, "ac1-confirm-delete-dialog");

        // Drive the real "Move safe items to trash" button.
        var confirmButton = UiE2E.Button(window, "Move safe");
        Assert.NotNull(confirmButton);
        await UiE2E.ClickAsync(confirmButton!);

        // Real effect: the duplicate's copies were trashed; the irreplaceable single copy is preserved.
        Assert.Equal(0, dup.Paths.Count(File.Exists));       // redundant copies moved to trash
        Assert.True(File.Exists(single.Paths[0]));            // protected single copy untouched
        // GF-1: a toast fired from the real completion.
        Assert.True(shell.ToastVisible);
        Assert.Contains("trash", shell.ToastMessage, StringComparison.OrdinalIgnoreCase);
        UiE2E.Screenshot(window, "ac1-after-delete");
    }

    [AvaloniaFact]
    public async Task Add_to_preset_button_adds_selected_packages_to_the_chosen_preset()
    {
        var (host, window, shell, lib, repo, dup, single) = await LaunchWithLibraryAsync();
        await using var _h = host;
        using var _r = repo;

        // Create a target preset the ops-bar flyout will list.
        using (var scope = host.Host.Services.CreateScope())
        {
            var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();
            await presets.CreateAsync("My preset", []);
        }
        await lib.RefreshAsync(); // reload so the preset appears in the flyout source
        UiE2E.Pump();
        var target = lib.Presets.First();

        // Select both packages and invoke the real AddToPreset command the flyout item is bound to.
        lib.SelectedItems.Add(lib.Items.First(i => i.PackageId == dup.PackageId));
        lib.SelectedItems.Add(lib.Items.First(i => i.PackageId == single.PackageId));
        await lib.AddToPresetCommand.ExecuteAsync(target.Id);
        UiE2E.Pump();

        // Real effect: the preset gained the two members.
        using (var verify = host.Host.Services.CreateScope())
        {
            var presets = verify.ServiceProvider.GetRequiredService<IPresetService>();
            var members = await presets.MembersAsync(target.Id);
            Assert.Equal(2, members.Count);
        }
        Assert.True(shell.ToastVisible);
        UiE2E.Screenshot(window, "ac2-added-to-preset");
    }

    [AvaloniaFact]
    public async Task Fix_encoding_button_is_wired_and_runs_over_the_selection()
    {
        var (host, window, shell, lib, repo, dup, _) = await LaunchWithLibraryAsync();
        await using var _h = host;
        using var _r = repo;

        lib.SelectedItems.Add(lib.Items.First(i => i.PackageId == dup.PackageId));
        var fixButton = UiE2E.Button(window, "Fix encoding");
        Assert.NotNull(fixButton);
        Assert.NotNull(fixButton!.Command); // AC-3: wired (was a no-op)
        await UiE2E.ClickAsync(fixButton);

        // The real command ran end-to-end and reported a result (these healthy vars need no fix → reported).
        Assert.NotNull(lib.LastActionMessage);
        Assert.Contains("Fixed", lib.LastActionMessage!, StringComparison.OrdinalIgnoreCase);
        UiE2E.Screenshot(window, "ac3-fix-encoding-ran");
    }
}
