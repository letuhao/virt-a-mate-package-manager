using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// doc 26 · G-1 — the library detail "★ Favorite" and "◎ Locate" buttons were rendered with no Command
/// (audit 25 §5.2, DEAD). These prove they now do real work.
/// </summary>
[Trait("Category", TestCategories.E2E)]
public class LibraryDetailActionTests
{
    // G-1.1 · Favorite toggles the flag through the real action service and persists it.
    [AvaloniaFact]
    public async Task Toggling_favorite_persists_and_flips_the_flag()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Fav.One.1", copyCount: 1);

        using var scope = host.Host.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<ILibraryQueryService>();
        var lib = new LibraryViewModel(
            query,
            actions: scope.ServiceProvider.GetService<ILibraryActionService>(),
            detail: scope.ServiceProvider.GetService<IPackageDetailQuery>());
        await lib.RefreshAsync();
        UiE2E.Pump();

        var entry = lib.Items.Single();
        Assert.False(entry.IsFavorite);
        lib.SelectedEntry = entry;

        await lib.ToggleFavoriteCommand.ExecuteAsync(null);
        UiE2E.Pump();

        Assert.True(lib.SelectedEntry!.IsFavorite); // in-memory row updated immediately
        var favPage = await query.GetPageAsync(new LibraryQuery(FavoritesOnly: true));
        Assert.Contains(favPage.Items, i => i.PackageId == entry.PackageId); // persisted to the catalog
    }

    // G-1.2 · Locate reveals the selected package's on-disk path (prefers the online copy).
    [Fact]
    public void Locate_reveals_the_selected_online_copy_path()
    {
        var reveal = new CapturingReveal();
        var lib = new LibraryViewModel(new StubLibraryQuery(), reveal: reveal)
        {
            SelectedDetail = new PackageDetail(
                1, "A.B.1", "AB1", null, 100, "Cold", 0,
                new[]
                {
                    new CopyDto(9, 3, @"E:\cold\A.B.1.var", 100, false, null), // offline copy
                    new CopyDto(1, 1, @"D:\hot\A.B.1.var", 100, true, null),   // online — preferred
                }),
        };

        lib.LocateCommand.Execute(null);

        Assert.Equal(@"D:\hot\A.B.1.var", reveal.LastPath);
    }

    private sealed class CapturingReveal : VarVault.App.Services.IFileReveal
    {
        public string? LastPath { get; private set; }
        public void Reveal(string path) => LastPath = path;
    }
}
