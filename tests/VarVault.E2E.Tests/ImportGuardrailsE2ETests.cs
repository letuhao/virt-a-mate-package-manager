using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Import;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;
using static VarVault.E2E.Tests.ImportFixtures;

namespace VarVault.E2E.Tests;

/// <summary>
/// Guardrail coverage for the import fixes (audit doc 30 §3/D1, §11, E5): D1 dedup-trust warnings for an unindexed
/// target repo; a graceful cancel mid-apply still records a partial run; and Apply blocks with a clear message when
/// the target repo is offline. Deterministic — temp catalog + temp target repo, nothing real touched.
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ImportGuardrailsE2ETests
{
    private static async Task<(Guid TargetId, string TargetPath, string ImportPath)> SeedAsync(TestHost host,
        TempDirectory targetDir, TempDirectory importDir, bool indexTarget)
    {
        // Target repo already holds a var on disk (so the "unindexed" signal has something to detect).
        WriteVar(targetDir.Path, "Existing.OnDisk.1.var", "Existing", "OnDisk", [("Custom/e.vam", "EEE")]);
        WriteVar(importDir.Path, "Fresh.Look.1.var", "Fresh", "Look", [("Custom/n.vam", "NEW")]);

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            targetId = (await repos.RegisterAsync(new RegisterRepositoryRequest("target", targetDir.Path))).Value.Id;
        }
        if (indexTarget)
            using (var scope = host.Host.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();
        return (targetId, targetDir.Path, importDir.Path);
    }

    [Fact]
    public async Task Unindexed_target_repo_yields_a_D1_dedup_trust_warning()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();
        var (targetId, _, importPath) = await SeedAsync(host, targetDir, importDir, indexTarget: false);

        using var scope = host.Host.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
        var session = await svc.ScanAsync(new ImportSpec([importPath], targetId), progress: null!);

        Assert.NotEmpty(session.Warnings);
        Assert.Contains(session.Warnings, w => w.Contains("index"));   // "chưa được index …"
    }

    [Fact]
    public async Task Indexed_target_repo_has_no_stale_warning()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();
        var (targetId, _, importPath) = await SeedAsync(host, targetDir, importDir, indexTarget: true);

        using var scope = host.Host.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
        var session = await svc.ScanAsync(new ImportSpec([importPath], targetId), progress: null!);

        Assert.DoesNotContain(session.Warnings, w => w.Contains("index"));
    }

    [Fact]
    public async Task Cancelled_apply_records_a_partial_run_and_cleans_temp()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();
        var (targetId, _, importPath) = await SeedAsync(host, targetDir, importDir, indexTarget: true);

        string tempRoot;
        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            var session = await svc.ScanAsync(new ImportSpec([importPath], targetId), progress: null!);
            tempRoot = session.TempRoot;

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // graceful cancel before any item is applied

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.ApplyAsync(session, cts.Token));
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            var history = await svc.HistoryAsync(10);
            Assert.Single(history);                              // a partial run WAS recorded (§11/E5)
            Assert.Equal(0, history[0].Copied);
            Assert.True(history[0].Skipped >= 1);                // the remainder is marked skipped
        }
        Assert.False(File.Exists(Path.Combine(targetDir.Path, "Fresh.Look.1.var"))); // nothing landed
        Assert.False(Directory.Exists(tempRoot));               // temp cleaned even on cancel
    }

    [Fact]
    public async Task Apply_blocks_with_a_clear_message_when_target_repo_is_offline()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();
        var (targetId, _, importPath) = await SeedAsync(host, targetDir, importDir, indexTarget: true);

        ImportSession session;
        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            session = await svc.ScanAsync(new ImportSpec([importPath], targetId), progress: null!);
            foreach (var it in session.Items.Where(i => i.Decision == ImportDecision.None))
                it.Decision = it.Recommendation;
        }

        // Take the repo offline (drive unplugged / share down).
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            var repo = await db.Repositories.FirstAsync(r => r.Id == targetId);
            repo.IsOnline = false;
            await db.SaveChangesAsync();
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ApplyAsync(session));
            Assert.Contains("offline", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
