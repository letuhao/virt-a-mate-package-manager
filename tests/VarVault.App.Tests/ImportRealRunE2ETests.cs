using System.IO;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Sdk.Import;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// Slice E (doc 31 Phase 7): drives the REAL Import screen through the shell over the real corpus + messy fixture —
/// scan → review → apply into a scratch target repo — and screenshots each stage. The real library is never mutated
/// (target is a temp repo). Skips when the corpus / fixture env vars aren't set.
/// </summary>
public sealed class ImportRealRunE2ETests
{
    [AvaloniaFact]
    public async Task Import_screen_scans_reviews_and_applies_the_real_fixture()
    {
        var corpus = TestCorpus.Primary;
        var fixture = Environment.GetEnvironmentVariable("VARVAULT_TEST_IMPORT");
        if (corpus is null || string.IsNullOrWhiteSpace(fixture) || !Directory.Exists(fixture))
            return;

        using var targetDir = new TempDirectory();
        await using var host = TestHost.Create(withPersistence: true);

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            await repos.RegisterAsync(new RegisterRepositoryRequest("corpus", corpus));
            targetId = (await repos.RegisterAsync(new RegisterRepositoryRequest("import-target", targetDir.Path))).Value.Id;
        }
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        var scope2 = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope2.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        UiE2E.Pump();

        // Navigate to the real Import screen.
        shell.Navigate("import");
        var import = (ImportViewModel)shell.ActiveScreen!;
        if (shell.PendingScreenLoad is { } load) await load;
        UiE2E.Pump();

        import.TargetRepo = import.Repositories.First(r => r.Id == targetId);
        import.AddSourcePath(fixture);

        // Scan the messy fixture (folders + archives).
        await import.ScanCommand.ExecuteAsync(null);
        UiE2E.Pump();
        Assert.True(import.TotalScanned > 0, "scan found no vars");
        Assert.True(import.ConflictCount + import.NamingCount + import.CorruptCount >= 1, "expected some review-lane items");
        UiE2E.Screenshot(window, "import-01-scan-table");

        // Gallery view.
        import.ToggleViewCommand.Execute(null);
        UiE2E.Pump();
        UiE2E.Screenshot(window, "import-02-gallery");
        import.ToggleViewCommand.Execute(null);

        // Select a conflict to show the resolver, then accept all recommendations.
        import.Selected = import.Items.FirstOrDefault(i => i.Lane == ImportLane.Conflict) ?? import.Items.FirstOrDefault();
        UiE2E.Pump();
        UiE2E.Screenshot(window, "import-03-resolver");

        Assert.False(import.CanApply);            // gated until reviewed
        import.AcceptAllCommand.Execute(null);
        UiE2E.Pump();
        Assert.Equal(0, import.ReviewRemaining);
        Assert.True(import.CanApply);

        // Apply into the scratch target repo.
        await import.ApplyCommand.ExecuteAsync(null);
        UiE2E.Pump();
        Assert.Contains("copy", import.StatusMessage ?? "");
        Assert.NotEmpty(Directory.GetFiles(targetDir.Path, "*.var", SearchOption.AllDirectories)); // real files landed
        UiE2E.Screenshot(window, "import-04-applied");

        // History overlay shows the run + the password/broken archive failures.
        await import.OpenHistoryCommand.ExecuteAsync(null);
        UiE2E.Pump();
        Assert.NotEmpty(import.History);
        UiE2E.Screenshot(window, "import-05-history");

        // The real corpus was never touched (files only landed in the temp target).
        Assert.False(File.Exists(Path.Combine(corpus, "ImportTest.FreshLook.1.var")));
        scope2.Dispose();
    }
}
