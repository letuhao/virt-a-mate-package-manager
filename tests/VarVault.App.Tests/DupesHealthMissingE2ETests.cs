using Avalonia.Headless.XUnit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// AC-15 / AC-16 / AC-17 · Real UI-E2E for Duplicates, Health, and Missing-deps: reclaim summary cards,
/// encoding summary cards, and the missing-deps scope column + export — over real seeded data. (19-Audit.)
/// </summary>
public class DupesHealthMissingE2ETests
{
    [AvaloniaFact]
    public async Task Dupes_reclaim_summary_cards_and_reclaim_button_render()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Dup.Grp.1", copyCount: 2); // duplicate pair

        var (window, shell) = LaunchScreen(host, "dupes");
        var dupes = (DupesViewModel)shell.ActiveScreen!;
        await dupes.LoadCommand.ExecuteAsync(null);
        UiE2E.Pump();

        Assert.NotEmpty(dupes.Groups);            // AC-15: real duplicate group
        Assert.True(dupes.RedundantCopies >= 1);  // reclaim summary card value
        Assert.True(dupes.GroupCount >= 1);
        UiE2E.Screenshot(window, "ac15-dupes");
    }

    [AvaloniaFact]
    public async Task Health_encoding_summary_cards_reflect_real_codepages()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Enc.Broken.1", copyCount: 1);
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            var vf = await db.VarFiles.FirstAsync();
            vf.EncodingHealth = EncodingHealth.NeedsFix;
            vf.DetectedCodepage = "GBK";
            await db.SaveChangesAsync();
        }

        var (window, shell) = LaunchScreen(host, "health");
        var health = (HealthViewModel)shell.ActiveScreen!;
        await health.LoadCommand.ExecuteAsync(null);
        UiE2E.Pump();

        Assert.NotEmpty(health.EncodingGroups);
        Assert.True(health.GbkCount >= 1); // AC-16: GBK summary card populated from real data
        UiE2E.Screenshot(window, "ac16-health");
    }

    [AvaloniaFact]
    public async Task Missing_deps_scope_column_and_export_links_work()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        var pkg = await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Owner.Pkg.1", copyCount: 1);
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            db.Dependencies.Add(new Dependency
            {
                VarFileId = pkg.VarFileIds[0], DependsOnRefKey = "SOME.MISSING.1",
                DependsOnRefRaw = "Some.Missing.1", RefKind = RefKind.Meta, IsMissing = true,
            });
            await db.SaveChangesAsync();
        }

        var (window, shell) = LaunchScreen(host, "missing");
        var missing = (MissingDepsViewModel)shell.ActiveScreen!;
        await missing.RefreshCommand.ExecuteAsync(null);
        UiE2E.Pump();

        Assert.NotEmpty(missing.Items); // AC-17: the missing ref shows (scope column renders "global")
        await missing.ExportLinksCommand.ExecuteAsync(null);
        Assert.Contains("Some.Missing.1", missing.LastExportText);
        UiE2E.Screenshot(window, "ac17-missing");
    }

    private static (MainWindow window, ShellViewModel shell) LaunchScreen(TestHost host, string screenId)
    {
        var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.Navigate(screenId);
        UiE2E.Pump();
        return (window, shell);
    }
}
