using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Common.Diagnostics;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;
using Xunit.Abstractions;

namespace VarVault.E2E.Tests;

/// <summary>
/// Instrumented benchmark of the three heavy pipeline phases — indexing (enumerate+inspect+upsert), read-model
/// refresh, dependency resolution, and preview/gallery extraction — over a real repo, with a per-phase report and a
/// linear extrapolation to a target library size. Not a blind timer: every number comes from a metric.
/// Env-gated: set <c>VARVAULT_BENCH_REPO</c> (defaults to D:\VarVault_test_repo). Optionally <c>VARVAULT_BENCH_TARGET_VARS</c>
/// (default 70000) and <c>VARVAULT_BENCH_TARGET_TB</c> (default 5).
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class IndexingBenchmark(ITestOutputHelper log)
{
    [SkippableFact]
    public async Task Benchmark_indexing_dependencies_and_previews()
    {
        var repoPath = Environment.GetEnvironmentVariable("VARVAULT_BENCH_REPO") ?? @"D:\VarVault_test_repo";
        Skip.If(!Directory.Exists(repoPath), $"benchmark repo not found: {repoPath} (set VARVAULT_BENCH_REPO)");
        var targetVars = long.TryParse(Environment.GetEnvironmentVariable("VARVAULT_BENCH_TARGET_VARS"), out var tv) ? tv : 70_000;
        var targetTb = double.TryParse(Environment.GetEnvironmentVariable("VARVAULT_BENCH_TARGET_TB"), NumberStyles.Any, CultureInfo.InvariantCulture, out var tt) ? tt : 5.0;

        // ── Ground truth on disk (so rates are per real var + per real byte). ─────────────────────────────
        var files = Directory.EnumerateFiles(repoPath, "*.var", SearchOption.AllDirectories).ToList();
        long totalBytes = 0;
        foreach (var f in files) { try { totalBytes += new FileInfo(f).Length; } catch { } }
        var varCount = files.Count;
        Skip.If(varCount == 0, "no .var files in the benchmark repo");

        await using var host = TestHost.Create(withPersistence: true);
        Guid repoId;
        using (var scope = host.Host.Services.CreateScope())
            repoId = (await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("bench", repoPath))).Value.Id;

        // ── Metric collectors (the report is built entirely from these). ──────────────────────────────────
        using var scanMs = new MetricCollector<double>(Telemetry.IndexScanDurationMs);
        using var refreshMs = new MetricCollector<double>(Telemetry.IndexRefreshDurationMs);
        using var resolveMs = new MetricCollector<double>(Telemetry.IndexResolveDurationMs);
        using var previewMs = new MetricCollector<double>(Telemetry.PreviewDurationMs);
        using var varsIndexed = new MetricCollector<long>(Telemetry.IndexVarsIndexed);
        using var previewsCount = new MetricCollector<long>(Telemetry.PreviewsExtracted);
        using var previewBytes = new MetricCollector<long>(Telemetry.PreviewBytesStored);

        // ── COLD run: first full index of the repo. ───────────────────────────────────────────────────────
        var wall = System.Diagnostics.Stopwatch.StartNew();
        IndexRunSummary summary;
        using (var scope = host.Host.Services.CreateScope())
            summary = await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();
        wall.Stop();

        // ── WARM run: re-index (everything fresh → skip path). ────────────────────────────────────────────
        var warm = System.Diagnostics.Stopwatch.StartNew();
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();
        warm.Stop();

        double Sum(MetricCollector<double> c) => c.GetMeasurementSnapshot().Sum(m => m.Value);
        long SumL(MetricCollector<long> c) => c.GetMeasurementSnapshot().Sum(m => m.Value);

        var scan = Sum(scanMs);
        var refresh = Sum(refreshMs);
        // resolve/preview fire on both cold+warm; the warm ones are ~0 work but appear as extra measurements —
        // take the max single measurement (the cold pass) so warm noise doesn't inflate the cold figure.
        var resolve = resolveMs.GetMeasurementSnapshot().DefaultIfEmpty().Max(m => m.Value);
        var preview = previewMs.GetMeasurementSnapshot().DefaultIfEmpty().Max(m => m.Value);
        var indexedVars = SumL(varsIndexed) / 2;                 // cold + warm each add; cold did the real work
        var previews = previewsCount.GetMeasurementSnapshot().DefaultIfEmpty().Max(m => m.Value);
        var thumbBytes = SumL(previewBytes);

        double MB(double bytes) => bytes / (1024.0 * 1024.0);
        double perVar(double ms) => varCount == 0 ? 0 : ms / varCount;
        string Est(double msPerVar) => TimeSpan.FromMilliseconds(msPerVar * targetVars).ToString(@"hh\:mm\:ss");

        var sb = new StringBuilder();
        void L(string s) { sb.AppendLine(s); log.WriteLine(s); }

        L("================ VarVault indexing benchmark ================");
        L($"GC mode            : {(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")} (the app ships Server GC; run with DOTNET_gcServer=1 to match)");
        L($"CPU                 : {Environment.ProcessorCount} logical");
        L($"Repo               : {repoPath}");
        L($"Vars on disk       : {varCount:N0}");
        L($"Total size         : {MB(totalBytes) / 1024:N2} GB   (avg {MB((double)totalBytes / Math.Max(1, varCount)):N1} MB/var)");
        L($"Catalog result     : indexed {summary.Indexed}, resolved {summary.Resolved}, missing {summary.Missing}, thumbnails {previews}");
        L("");
        L("---- COLD (first full index) ----");
        L($"Wall clock (index+resolve+usage) : {wall.Elapsed.TotalSeconds,8:N2} s");
        L($"  Phase index/scan (open+inspect+upsert) : {scan,10:N0} ms  ({perVar(scan),6:N2} ms/var, {varCount / Math.Max(0.001, scan / 1000):N0} vars/s)");
        L($"  Phase read-model refresh               : {refresh,10:N0} ms  ({perVar(refresh),6:N2} ms/var)");
        L($"  Phase dependency resolve               : {resolve,10:N0} ms  ({perVar(resolve),6:N2} ms/var)");
        L($"  Phase preview/gallery extract          : {preview,10:N0} ms  ({(previews > 0 ? preview / previews : 0),6:N2} ms/preview, {previews} previews)");
        L($"  Thumbnail bytes stored                 : {MB(thumbBytes),8:N1} MB  (avg {(previews > 0 ? thumbBytes / previews / 1024.0 : 0):N1} KB/thumb)");
        L($"  Scan throughput (payload)              : {MB(totalBytes) / Math.Max(0.001, wall.Elapsed.TotalSeconds):N1} MB/s (wall)");
        L("");
        L("---- WARM (re-index, all-fresh skip path) ----");
        L($"Wall clock : {warm.Elapsed.TotalSeconds,8:N2} s   ({warm.Elapsed.TotalMilliseconds / Math.Max(1, varCount):N3} ms/var — enumerate+stat only)");
        L("");
        L($"================ EXTRAPOLATION → {targetVars:N0} vars / {targetTb:N1} TB ================");
        L("(index/refresh/resolve/preview scale with var COUNT — indexing reads zip central directories + meta,");
        L(" not full payloads, so total TB mostly affects the directory walk, not per-var cost.)");
        var totalPerVar = perVar(scan) + perVar(refresh) + perVar(resolve) + (previews > 0 ? preview / varCount : 0);
        L($"  Est. index/scan   : {Est(perVar(scan))}");
        L($"  Est. refresh      : {Est(perVar(refresh))}");
        L($"  Est. resolve      : {Est(perVar(resolve))}");
        L($"  Est. previews     : {Est(previews > 0 ? preview / varCount : 0)}");
        L($"  Est. TOTAL (cold) : {Est(totalPerVar)}   ({totalPerVar:N2} ms/var × {targetVars:N0})");
        L($"  Est. re-index     : {TimeSpan.FromMilliseconds(warm.Elapsed.TotalMilliseconds / Math.Max(1, varCount) * targetVars):hh\\:mm\\:ss} (warm/skip)");
        L("=============================================================");

        // Persist the report next to the repo drive's temp for the user to read.
        var reportPath = Path.Combine(Path.GetTempPath(), $"varvault-index-benchmark-{varCount}vars.txt");
        try { File.WriteAllText(reportPath, sb.ToString()); log.WriteLine($"\nReport written: {reportPath}"); } catch { }

        Assert.True(summary.Indexed > 0, "cold index should have indexed vars");
    }
}
