using System.IO;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// AC-20 / AC-21 / AC-23 · Real UI-E2E for Presets (export/import), Settings (per-tab content + persist), and
/// Trash (bulk restore), driven through the real shell over real data. (19-Audit.)
/// </summary>
public class PresetsSettingsTrashE2ETests
{
    [AvaloniaFact]
    public async Task Presets_export_lists_members_and_import_opens_the_dialog()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Creator.Member.1", copyCount: 1);
        using (var scope = host.Host.Services.CreateScope())
        {
            var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();
            var p = await presets.CreateAsync("My preset", []);
            await presets.AddMemberAsync(p.Value.Id, "Creator.Member.1");
        }

        var scope2 = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope2.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.Navigate("presets");
        UiE2E.Pump();
        var presetsVm = (PresetsViewModel)shell.ActiveScreen!;
        await presetsVm.LoadCommand.ExecuteAsync(null);
        UiE2E.Pump();
        presetsVm.Selected = presetsVm.Presets.First();
        for (var i = 0; i < 5 && presetsVm.Members.Count == 0; i++) { UiE2E.Pump(); await Task.Delay(20); }

        var exportPath = Path.Combine(Path.GetTempPath(), $"varvault-export-{Guid.NewGuid():N}.txt");
        presetsVm.SaveTxtPicker = _ => Task.FromResult<string?>(exportPath);
        await presetsVm.ExportCommand.ExecuteAsync(null);
        Assert.Contains("Creator.Member.1", presetsVm.LastExportText); // AC-20
        Assert.True(File.Exists(exportPath));
        File.Delete(exportPath);

        var importPath = Path.Combine(Path.GetTempPath(), $"varvault-import-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(importPath, "Creator.Imported.1\n");
        presetsVm.OpenTxtPicker = () => Task.FromResult<string?>(importPath);
        await presetsVm.ImportTxtCommand.ExecuteAsync(null);
        UiE2E.Pump();
        Assert.True(shell.Dialogs.IsOpen);
        Assert.IsType<PresetEditViewModel>(shell.Dialogs.Current);
        File.Delete(importPath);
        UiE2E.Screenshot(window, "ac20-presets");
    }

    [AvaloniaFact]
    public async Task Settings_five_tabs_switch_content_and_new_fields_persist()
    {
        await using var host = TestHost.Create(withPersistence: true);
        var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.Navigate("settings");
        UiE2E.Pump();
        var settings = (SettingsViewModel)shell.ActiveScreen!;
        await settings.LoadCommand.ExecuteAsync(null);
        UiE2E.Pump();

        Assert.Equal(5, settings.Tabs.Count);
        Assert.True(settings.IsGeneralTab);
        settings.SelectedTabIndex = 1;
        Assert.True(settings.IsTiersTab);       // AC-21: tab switching changes content
        Assert.False(settings.IsGeneralTab);

        settings.HotThresholdDays = "45";
        await settings.SaveCommand.ExecuteAsync(null);
        UiE2E.Screenshot(window, "ac21-settings");

        // Persisted: reload a fresh VM reads it back.
        await settings.LoadCommand.ExecuteAsync(null);
        Assert.Equal("45", settings.HotThresholdDays);
    }

    [AvaloniaFact]
    public async Task Trash_bulk_restore_selected_restores_the_checked_rows()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        var dup = await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Trash.Dup.1", copyCount: 2);
        // Create a real trash item by deleting one redundant copy.
        using (var scope = host.Host.Services.CreateScope())
        {
            var actions = scope.ServiceProvider.GetRequiredService<ILibraryActionService>();
            var res = await actions.DeleteAsync([dup.VarFileIds[1]]);
            Assert.Equal(1, res.Succeeded);
        }

        var scope2 = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope2.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.Navigate("trash");
        UiE2E.Pump();
        var trash = (TrashViewModel)shell.ActiveScreen!;
        await trash.LoadCommand.ExecuteAsync(null);
        UiE2E.Pump();

        Assert.NotEmpty(trash.Items);
        // AC-23: check a row and bulk-restore it.
        trash.ToggleSelectionCommand.Execute(trash.Items[0]);
        Assert.Single(trash.SelectedItems);
        await trash.RestoreSelectedCommand.ExecuteAsync(null);
        UiE2E.Pump();
        Assert.Contains("Restored", trash.StatusMessage);
        Assert.Empty(trash.Items); // the trashed item was restored out of trash
        UiE2E.Screenshot(window, "ac23-trash");
    }
}
