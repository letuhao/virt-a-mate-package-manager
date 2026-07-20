using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Sdk.Import;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Repositories;
using VarVault.Sdk.Settings;
using VarVault.TestKit;
using Xunit.Abstractions;

namespace VarVault.E2E.Tests;

/// <summary>
/// REAL-DATA end-to-end run of the whole import pipeline over the user's own folders — a genuine (mutating) import
/// from a messy source into a real target repo, optionally activating the result into a real VaM install. Exercises
/// every audit fix: D1 dedup-trust warning (unindexed target), classify → review → durable apply → index → history,
/// and activate-after. Env-gated so it never runs in normal CI:
/// <list type="bullet">
///   <item><c>VARVAULT_REAL_IMPORT_SOURCE</c> — the messy folder to import (e.g. D:\Vam_Installer).</item>
///   <item><c>VARVAULT_REAL_IMPORT_TARGET</c> — the target repo to import INTO (e.g. D:\VarVault_test_repo). <b>Mutated.</b></item>
///   <item><c>VARVAULT_REAL_VAM</c> — optional VaM game folder (e.g. F:\VaM_1.20.77.9) for activate-after. <b>Mutated.</b></item>
/// </list>
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ImportRealDataRunE2ETests(ITestOutputHelper log)
{
    [SkippableFact]
    public async Task Real_import_source_into_target_repo_end_to_end()
    {
        var source = Environment.GetEnvironmentVariable("VARVAULT_REAL_IMPORT_SOURCE");
        var target = Environment.GetEnvironmentVariable("VARVAULT_REAL_IMPORT_TARGET");
        var vam = Environment.GetEnvironmentVariable("VARVAULT_REAL_VAM");
        Skip.If(string.IsNullOrWhiteSpace(source) || !Directory.Exists(source), "set VARVAULT_REAL_IMPORT_SOURCE to a real folder");
        Skip.If(string.IsNullOrWhiteSpace(target) || !Directory.Exists(target), "set VARVAULT_REAL_IMPORT_TARGET to a real repo folder");

        await using var host = TestHost.Create(withPersistence: true);
        log.WriteLine($"Source : {source}");
        log.WriteLine($"Target : {target}");
        log.WriteLine($"VaM    : {vam ?? "(none — activate-after skipped)"}");

        // Register the real target repo (do NOT index yet — proves the D1 unindexed-target warning).
        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            targetId = (await repos.RegisterAsync(new RegisterRepositoryRequest("import-target", target!))).Value.Id;
        }

        // ── D1: scan against the UNINDEXED target → expect a dedup-trust warning. ─────────────────────────
        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            var stale = await svc.ScanAsync(new ImportSpec([source!], targetId), progress: null!);
            log.WriteLine($"[D1] pre-index warnings: {(stale.Warnings.Count == 0 ? "(none)" : string.Join(" | ", stale.Warnings))}");
            Assert.Contains(stale.Warnings, w => w.Contains("index"));   // target has vars on disk but no catalog rows
        }

        // Index the whole library so dedup is trustworthy.
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        // Optional activate-after into the real VaM install.
        var activate = !string.IsNullOrWhiteSpace(vam) && Directory.Exists(vam);
        if (activate)
            using (var scope = host.Host.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<ISettingsService>().SetAsync(SettingKeys.VamPath, vam!);

        // ── Real scan → review → apply. ──────────────────────────────────────────────────────────────────
        var before = Directory.GetFiles(target!, "*.var", SearchOption.AllDirectories).Length;
        ApplyResult result;
        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            var session = await svc.ScanAsync(new ImportSpec([source!], targetId, ActivateAfter: activate), progress: null!);

            Assert.True(session.Items.Count > 0, "scan found no vars in the source");
            Assert.DoesNotContain(session.Warnings, w => w.Contains("index")); // target now indexed → no stale warning
            log.WriteLine($"Scanned {session.Items.Count} vars from {session.Sources.Count} source(s).");
            foreach (var g in session.Items.GroupBy(i => i.Lane).OrderBy(g => g.Key))
                log.WriteLine($"  {g.Key,-9} {g.Count()}");
            foreach (var f in session.Sources.Where(s => s.Status != ImportSourceStatus.Ok))
                log.WriteLine($"  failed source: {Path.GetFileName(f.Path)} — {f.FailReason}");

            // Accept every recommendation for the review lanes (New/Exact/CJK are already decided).
            foreach (var item in session.Items.Where(i => i.Decision == ImportDecision.None))
                item.Decision = item.Recommendation;
            Assert.DoesNotContain(session.Items, i => i.Decision == ImportDecision.None); // nothing left unresolved

            result = await svc.ApplyAsync(session);
        }

        var after = Directory.GetFiles(target!, "*.var", SearchOption.AllDirectories).Length;
        log.WriteLine($"Apply → copied {result.Copied} · fixed {result.Fixed} · renamed {result.Renamed} · " +
                      $"skipped {result.Skipped} · discarded {result.Discarded} · failed {result.Failed}");
        log.WriteLine($"Target repo .var count: {before} → {after}");

        // The pipeline ran to completion and history reflects it. (First run copies; a repeat run dedups → all skipped.)
        Assert.True(result.Copied + result.Renamed + result.Skipped + result.Discarded > 0, "no items were acted on");
        Assert.Equal(before + result.Copied + result.Renamed, after);   // exactly the landed vars appeared on disk

        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            var history = await svc.HistoryAsync(5);
            Assert.NotEmpty(history);
            Assert.Equal(result.RunId, history[0].Id);
            log.WriteLine($"History run {history[0].Id}: {history[0].SourceSummary} · {history[0].Outcomes.Count} outcomes · " +
                          $"{history[0].FailedSources.Count} failed sources");
        }

        if (activate)
            log.WriteLine("Activate-after ran into the real VaM install (see profile switch dir).");
    }
}
