using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// AC-6 · Real UI-E2E for the alias dialog: the previously-unbound "map to owned" search now drives
/// <see cref="AliasViewModel.OwnedPackageId"/>, so saving persists a real target (not id 0). Launches the
/// shell, opens the dialog, searches the real owned library, picks a match, clicks the real Save button,
/// and asserts the alias persisted with the chosen package id. (19-Audit.)
/// </summary>
public class AliasDialogE2ETests
{
    [AvaloniaFact]
    public async Task Save_persists_the_alias_with_the_selected_owned_package_id()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        var owned = await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Owner.Target.1", copyCount: 1);

        var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        UiE2E.Pump();

        // Open the alias dialog for an unresolved missing ref.
        var alias = new AliasViewModel(
            scope.ServiceProvider.GetRequiredService<IAliasService>(),
            scope.ServiceProvider.GetRequiredService<ILibraryQueryService>())
        {
            MissingRef = "SomeCreator.MissingDep.1",
        };
        shell.Dialogs.Show(alias);
        UiE2E.Pump();

        // Save is gated until a real owned package is chosen (was previously savable with id 0).
        Assert.False(alias.CanSave);

        // Type into the real owned-package search → real query over the owned library.
        alias.OwnedQuery = "Owner";
        if (alias.SearchTask is not null)
            await alias.SearchTask;
        Assert.NotEmpty(alias.OwnedMatches);

        // Pick the match (what the AutoCompleteBox SelectedItem binding sets).
        alias.SelectedOwned = alias.OwnedMatches.First(m => m.PackageId == owned.PackageId);
        Assert.True(alias.CanSave);
        Assert.Equal(owned.PackageId, alias.OwnedPackageId); // AC-6: input captured (no longer 0)
        UiE2E.Screenshot(window, "ac6-alias-selected");

        // Click the real Save button.
        var saveButton = UiE2E.Button(window, "Save alias");
        Assert.NotNull(saveButton);
        await UiE2E.ClickAsync(saveButton!);

        Assert.Equal("Alias saved", alias.StatusMessage);

        // Real effect: the alias persisted resolving to the chosen owned package.
        using var verify = host.Host.Services.CreateScope();
        var aliases = verify.ServiceProvider.GetRequiredService<IAliasService>();
        var list = await aliases.ListAsync();
        Assert.Contains(list, a => a.MissingRef == "SomeCreator.MissingDep.1" && a.ResolvedPackageId == owned.PackageId);
    }
}
