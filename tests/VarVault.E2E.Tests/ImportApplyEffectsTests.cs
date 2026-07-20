using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using VarVault.Common.Diagnostics;
using VarVault.Sdk.Events;
using VarVault.Sdk.Import;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Presets;
using VarVault.Sdk.Repositories;
using VarVault.Sdk.Settings;
using VarVault.TestKit;
using static VarVault.E2E.Tests.ImportFixtures;

namespace VarVault.E2E.Tests;

/// <summary>
/// Slice C follow-ups (doc 31 · 5.7 event/telemetry, 5.9 activate-after): a successful apply publishes
/// <see cref="VarsImported"/>, records the import metrics, and — when <c>ActivateAfter</c> is set — routes the
/// just-imported vars through the activation flow (proven here by the durable "Imported" preset it builds; the
/// on-disk symlink build is exercised env-gated by the app real-run test).
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ImportApplyEffectsTests
{
    [Fact]
    public async Task Apply_publishes_VarsImported_and_records_metrics()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();

        Guid targetId = await RegisterTargetAsync(host, targetDir.Path);
        WriteVar(importDir.Path, "Fresh.Look.1.var", "Fresh", "Look", [("Custom/n.vam", "N")]);

        using var applied = new MetricCollector<long>(Telemetry.ImportsApplied);
        using var copiedMetric = new MetricCollector<long>(Telemetry.ImportVarsCopied);

        VarsImported? captured = null;
        using var _ = host.Host.Services.GetRequiredService<IEventBus>().Subscribe<VarsImported>(e => captured = e);

        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            var session = await svc.ScanAsync(new ImportSpec([importDir.Path], targetId));
            var result = await svc.ApplyAsync(session);
            Assert.Equal(1, result.Copied);
        }

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Copied);
        Assert.False(captured.ActivatedAfter);          // ActivateAfter was not requested
        Assert.True(applied.GetMeasurementSnapshot().Count >= 1, "imports.applied metric should have fired");
        Assert.True(copiedMetric.GetMeasurementSnapshot().Count >= 1, "imports.vars_copied metric should have fired");
    }

    [Fact]
    public async Task Activate_after_import_routes_through_the_activation_flow()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();

        Guid targetId = await RegisterTargetAsync(host, targetDir.Path);
        WriteVar(importDir.Path, "Fresh.Look.1.var", "Fresh", "Look", [("Custom/n.vam", "N")]);

        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            // ActivateAfter set. With no VaM path configured the link build is a safe no-op, but the import
            // must still route into the durable "Imported" preset — proving 5.9 is wired end-to-end.
            var session = await svc.ScanAsync(new ImportSpec([importDir.Path], targetId, ActivateAfter: true));
            var result = await svc.ApplyAsync(session);
            Assert.Equal(1, result.Copied);
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();
            var imported = (await presets.ListAsync()).FirstOrDefault(p => p.Name == "Imported");
            Assert.NotNull(imported);
            var members = await presets.MembersAsync(imported!.Id);
            Assert.Contains("Fresh.Look.1", members);
        }
    }

    [Fact]
    public async Task Activate_after_is_not_triggered_when_flag_is_off()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();

        Guid targetId = await RegisterTargetAsync(host, targetDir.Path);
        WriteVar(importDir.Path, "Fresh.Look.1.var", "Fresh", "Look", [("Custom/n.vam", "N")]);

        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            var session = await svc.ScanAsync(new ImportSpec([importDir.Path], targetId, ActivateAfter: false));
            await svc.ApplyAsync(session);
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();
            Assert.DoesNotContain(await presets.ListAsync(), p => p.Name == "Imported");
        }
    }

    [Fact]
    public async Task Apply_persists_per_item_outcomes()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();

        Guid targetId = await RegisterTargetAsync(host, targetDir.Path);
        WriteVar(importDir.Path, "Fresh.Look.1.var", "Fresh", "Look", [("Custom/n.vam", "N")]);   // New → Import
        WriteBadZip(Path.Combine(importDir.Path, "broken.var"));                                    // Corrupt → Discard

        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            var session = await svc.ScanAsync(new ImportSpec([importDir.Path], targetId));
            foreach (var i in session.Items.Where(i => i.Decision == ImportDecision.None))
                i.Decision = i.Recommendation;
            await svc.ApplyAsync(session);
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            var run = (await svc.HistoryAsync(1)).Single();
            Assert.Equal(2, run.Outcomes.Count);                                      // one row per applied item (§8)
            var fresh = run.Outcomes.Single(o => o.FileName == "Fresh.Look.1.var");
            Assert.True(fresh.Ok);
            Assert.Equal(ImportDecision.Import, fresh.Decision);
            var broken = run.Outcomes.Single(o => o.FileName == "broken.var");
            Assert.Equal(ImportLane.Corrupt, broken.Lane);
            Assert.Equal(ImportDecision.Discard, broken.Decision);
        }
    }

    [Fact]
    public async Task Sweep_removes_orphaned_temp_dirs()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var tempBase = new TempDirectory();

        // Point the import temp base at our scratch dir, then plant an orphan session dir the way a crash would.
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ISettingsService>().SetAsync("import.temp_dir", tempBase.Path);

        var orphan = Path.Combine(tempBase.Path, "import", "deadbeefdeadbeef");
        Directory.CreateDirectory(orphan);
        Assert.True(Directory.Exists(orphan));

        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IImportService>().SweepTempWorkspacesAsync();

        Assert.False(Directory.Exists(orphan));   // startup sweep cleaned it (§6/E5)
    }

    [Fact]
    public async Task Scan_records_scanned_metric()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();
        Guid targetId = await RegisterTargetAsync(host, targetDir.Path);
        WriteVar(importDir.Path, "A.B.1.var", "A", "B", [("Custom/x.vam", "X")]);

        using var scanned = new MetricCollector<long>(Telemetry.ImportScanned);
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IImportService>()
                .ScanAsync(new ImportSpec([importDir.Path], targetId));

        Assert.True(scanned.GetMeasurementSnapshot().Count >= 1, "imports.scanned metric should have fired");
    }

    private static async Task<Guid> RegisterTargetAsync(TestHost host, string targetPath)
    {
        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            targetId = (await repos.RegisterAsync(new RegisterRepositoryRequest("target", targetPath))).Value.Id;
        }
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();
        return targetId;
    }
}
