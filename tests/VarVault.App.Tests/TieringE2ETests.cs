using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// AC-14 · Real UI-E2E for Tiering: class-card bars reflect real counts, the misplaced table shows the Size
/// column, and per-row "Plan…" opens the migrate dialog — driven through the real shell. (19-Audit.)
/// </summary>
public class TieringE2ETests
{
    [AvaloniaFact]
    public async Task Class_bars_size_column_and_plan_button_work()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        // Cold-class packages sitting on Tier 1 → misplaced (should move to a colder tier).
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Cold.One.1", copyCount: 1);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 2, "Cold.Two.1", copyCount: 1);

        var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.Navigate("tiering");
        UiE2E.Pump();
        var tiering = (TieringViewModel)shell.ActiveScreen!;
        await tiering.LoadCommand.ExecuteAsync(null);
        UiE2E.Pump();

        // AC-14: real class counts drive the card bars.
        Assert.NotNull(tiering.Counts);
        Assert.Equal(2, tiering.Counts!.Cold);
        Assert.True(tiering.ColdFraction > 0);

        // AC-14: misplaced rows carry a real Size.
        Assert.NotEmpty(tiering.Misplaced);
        Assert.All(tiering.Misplaced, m => Assert.True(m.SizeBytes > 0));
        UiE2E.Screenshot(window, "ac14-tiering");

        // AC-14: "Plan…" / "Review migration plan" opens the migrate dialog.
        tiering.PlanCommand.Execute(null);
        UiE2E.Pump();
        Assert.True(shell.Dialogs.IsOpen);
        Assert.IsType<MigrateViewModel>(shell.Dialogs.Current);
    }
}
