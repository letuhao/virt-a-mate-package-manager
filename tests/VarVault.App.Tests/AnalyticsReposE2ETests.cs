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
/// AC-18 / AC-19 · Real UI-E2E for Analytics (usage-over-time sparkline) and Repositories (per-card Tier
/// override + Edit), driven through the real shell over real data. (19-Audit.)
/// </summary>
public class AnalyticsReposE2ETests
{
    [AvaloniaFact]
    public async Task Analytics_sparkline_points_are_built_from_real_data()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "A.One.1", copyCount: 1);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 2, "B.Two.1", copyCount: 1);

        var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.Navigate("analytics");
        UiE2E.Pump();
        var analytics = (AnalyticsViewModel)shell.ActiveScreen!;
        await analytics.RefreshCommand.ExecuteAsync(null);
        UiE2E.Pump();

        Assert.NotEmpty(analytics.ByType);              // real space-by-type data
        Assert.NotEmpty(analytics.SparkBars); // AC-18: sparkline card renders bars from real data
        UiE2E.Screenshot(window, "ac18-analytics");
    }

    [AvaloniaFact]
    public async Task Repository_tier_override_button_persists_the_new_tier()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path); // seeded at Tier 1

        var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.Navigate("repos");
        UiE2E.Pump();
        var repos = (RepositoriesViewModel)shell.ActiveScreen!;
        await repos.LoadCommand.ExecuteAsync(null);
        UiE2E.Pump();

        var card = repos.Repositories.First();
        Assert.Equal(1, card.Tier);

        // AC-19: the "Tier ▾ → T3" menu item runs the real SetTier command (BE-G2).
        await repos.SetTierCommand.ExecuteAsync(card.TierArg3);
        UiE2E.Pump();
        Assert.Equal(3, repos.Repositories.First().Tier);

        // Persisted in the catalog.
        using var verify = host.Host.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var tier = await db.Repositories.Where(r => r.Id == repoId).Select(r => r.Tier).FirstAsync();
        Assert.Equal(3, tier);
        UiE2E.Screenshot(window, "ac19-repos");
    }
}
