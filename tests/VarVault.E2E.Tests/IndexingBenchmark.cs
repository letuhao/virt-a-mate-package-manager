using System.Globalization;
using System.Diagnostics;
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
/// Instrumented end-to-end benchmark of the stream-ingest pipeline (discovery → one-handle inspect →
/// batched persist → refresh → paged resolve) over a REAL repository, with a per-phase report and a
/// linear extrapolation to a target library size. Every number comes from a metric, not a blind timer.
/// Env-gated: set <c>VARVAULT_BENCH_REPO</c> to the repo path (no hard-coded default). Optionally
/// <c>VARVAULT_BENCH_TARGET_VARS</c> (default 70000) and <c>VARVAULT_BENCH_TARGET_TB</c> (default 5).
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class IndexingBenchmark(ITestOutputHelper log)
{
    [SkippableFact]
    public async Task Benchmark_indexing_dependencies_and_previews()
    {
        var repoPath = Environment.GetEnvironmentVariable("VARVAULT_BENCH_REPO");
        Skip.If(string.IsNullOrWhiteSpace(repoPath), "set VARVAULT_BENCH_REPO to a real repo path to run the benchmark");
        Skip.If(!Directory.Exists(repoPath), $"benchmark repo not found: {repoPath}");
        var targetVars = long.TryParse(Environment.GetEnvironmentVariable("VARVAULT_BENCH_TARGET_VARS"), out var tv) ? tv : 70_000;
        var targetTb = double.TryParse(Environment.GetEnvironmentVariable("VARVAULT_BENCH_TARGET_TB"), NumberStyles.Any, CultureInfo.InvariantCulture, out var tt) ? tt : 5.0;

        // ── Ground truth on disk (so rates are per real var + per real byte). ─────────────────────────────
        var files = Directory.EnumerateFiles(repoPath!, "*.var", SearchOption.AllDirectories).ToList();
        long totalBytes = 0;
        foreach (var f in files) { try { totalBytes += new FileInfo(f).Length; } catch { } }
        var varCount = files.Count;
        Skip.If(varCount == 0, "no .var files in the benchmark repo");

        await using var host = TestHost.Create(withPersistence: true);
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("bench", repoPath!));

        // ── Metric collectors (the report is built entirely from these). ──────────────────────────────────
        using var discoveryMs = new MetricCollector<double>(Telemetry.IndexDiscoveryDurationMs);
        using var ingestMs = new MetricCollector<double>(Telemetry.IndexIngestDurationMs);
        using var inspectMs = new MetricCollector<double>(Telemetry.IndexInspectDurationMs);
        using var writeMs = new MetricCollector<double>(Telemetry.IndexWriteDurationMs);
        using var refreshMs = new MetricCollector<double>(Telemetry.IndexRefreshDurationMs);
        using var resolveMs = new MetricCollector<double>(Telemetry.IndexResolveDurationMs);
        using var varsIndexed = new MetricCollector<long>(Telemetry.IndexVarsIndexed);
        using var varsSkipped = new MetricCollector<long>(Telemetry.IndexVarsSkipped);
        using var varsFailed = new MetricCollector<long>(Telemetry.IndexVarsFailed);
        using var previewsCount = new MetricCollector<long>(Telemetry.PreviewsExtracted);
        using var previewBytes = new MetricCollector<long>(Telemetry.PreviewBytesStored);
        using var writesCompleted = new MetricCollector<long>(Telemetry.WritesCompleted);
        using var lockWaitMs = new MetricCollector<double>(Telemetry.WriteLockWaitDurationMs);
        using var lockHoldMs = new MetricCollector<double>(Telemetry.WriteLockHoldDurationMs);
        using var batchItems = new MetricCollector<int>(Telemetry.IndexPersistBatchItems);
        using var batchBytes = new MetricCollector<long>(Telemetry.IndexPersistBatchBytes);

        double Sum(MetricCollector<double> c) => c.GetMeasurementSnapshot().Sum(m => m.Value);
        long SumL(MetricCollector<long> c) => c.GetMeasurementSnapshot().Sum(m => m.Value);

        // ── COLD run: first full index (discovery + one-handle ingest + thumbs + refresh + resolve). ──────
        var wall = System.Diagnostics.Stopwatch.StartNew();
        await using var memory = new ProcessMemorySampler();
        IndexRunSummary summary;
        using (var scope = host.Host.Services.CreateScope())
            summary = await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();
        wall.Stop();
        await memory.StopAsync();

        // Snapshot cold numbers BEFORE the warm run so warm noise can't leak into them.
        var coldDiscovery = Sum(discoveryMs);
        var coldIngest = Sum(ingestMs);
        var coldInspect = Sum(inspectMs);       // per-var, summed across DOP workers
        var coldWrite = Sum(writeMs);           // per-var, summed
        var coldRefresh = Sum(refreshMs);
        var coldResolve = Sum(resolveMs);
        var coldIndexed = SumL(varsIndexed);
        var coldFailed = SumL(varsFailed);
        var coldPreviews = SumL(previewsCount);
        var coldThumbBytes = SumL(previewBytes);
        var coldWrites = SumL(writesCompleted);
        var coldLockWait = Sum(lockWaitMs);
        var coldLockHold = Sum(lockHoldMs);
        var coldLockHolds = lockHoldMs.GetMeasurementSnapshot().Select(m => m.Value).Order().ToArray();
        var coldBatchItems = batchItems.GetMeasurementSnapshot().Select(m => m.Value).ToArray();
        var coldBatchBytes = batchBytes.GetMeasurementSnapshot().Select(m => m.Value).ToArray();

        // ── WARM run: re-index (everything fresh → skip path). ────────────────────────────────────────────
        var warm = System.Diagnostics.Stopwatch.StartNew();
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();
        warm.Stop();
        var warmSkipped = SumL(varsSkipped);

        double MB(double bytes) => bytes / (1024.0 * 1024.0);
        double PerVar(double ms) => varCount == 0 ? 0 : ms / varCount;
        string Est(double msPerVar) => TimeSpan.FromMilliseconds(msPerVar * targetVars).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        void L(string s) { sb.AppendLine(s); log.WriteLine(s); }

        L("================ VarVault stream-indexing benchmark (A12 pipeline) ================");
        L($"GC mode            : {(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")}");
        L($"CPU                : {Environment.ProcessorCount} logical");
        L($"Repo               : {repoPath}");
        L($"Vars on disk       : {varCount:N0}");
        L($"Total size         : {MB(totalBytes) / 1024:N2} GB   (avg {MB((double)totalBytes / Math.Max(1, varCount)):N1} MB/var)");
        L($"Catalog result     : indexed {summary.Indexed}, skipped {summary.Skipped}, resolved {summary.Resolved}, missing {summary.Missing}");
        L($"Ingest health      : {coldIndexed} raw-stored, {coldFailed} failed, {coldPreviews} thumbnails ({MB(coldThumbBytes):N1} MB)");
        L($"Write queue        : {coldWrites:N0} serialized write actions (cold)");
        L($"Peak memory        : working set {MB(memory.PeakWorkingSetBytes):N0} MB, private {MB(memory.PeakPrivateBytes):N0} MB");
        L($"Managed memory     : peak {MB(memory.PeakManagedHeapBytes):N0} MB, retained {MB(memory.RetainedManagedHeapBytes):N0} MB, " +
          $"allocated {MB(memory.TotalAllocatedBytes):N0} MB (peak LOH {MB(memory.PeakLohBytes):N0} MB)");
        L($"GC collections     : gen0 {memory.Gen0Collections}, gen1 {memory.Gen1Collections}, gen2 {memory.Gen2Collections}");
        L($"Writer lock        : wait {coldLockWait:N0} ms; hold {coldLockHold:N0} ms total, " +
          $"p95 {Percentile(coldLockHolds, 0.95):N0} ms, max {(coldLockHolds.Length > 0 ? coldLockHolds[^1] : 0):N0} ms");
        if (coldBatchItems.Length > 0)
            L($"Persist batches    : {coldBatchItems.Length:N0}, avg {coldBatchItems.Average():N1} vars, max {coldBatchItems.Max():N0}; " +
              $"avg {MB(coldBatchBytes.Average()):N2} MB, max {MB(coldBatchBytes.Max()):N2} MB");
        L("");
        L("---- COLD (first full index) ----");
        L($"Wall clock (discover+ingest+refresh+resolve+usage) : {wall.Elapsed.TotalSeconds,8:N2} s");
        L($"  Phase discovery (enumerate+upsert facts)  : {coldDiscovery,10:N0} ms  ({PerVar(coldDiscovery),6:N2} ms/var)");
        L($"  Phase ingest (claim+inspect+persist wall) : {coldIngest,10:N0} ms  ({PerVar(coldIngest),6:N2} ms/var, {varCount / Math.Max(0.001, coldIngest / 1000):N0} vars/s)");
        L($"    · inspect one-handle (Σ across workers) : {coldInspect,10:N0} ms  ({(coldIndexed > 0 ? coldInspect / coldIndexed : 0),6:N2} ms/var)");
        L($"    · catalog write (Σ batched actions)     : {coldWrite,10:N0} ms  ({(coldIndexed > 0 ? coldWrite / coldIndexed : 0),6:N2} ms/var, batch N={VarVault.Domain.Indexing.IngestLimits.PersistBatchSize})");
        L($"  Phase read-model refresh                  : {coldRefresh,10:N0} ms  ({PerVar(coldRefresh),6:N2} ms/var)");
        L($"  Phase dependency resolve (paged)          : {coldResolve,10:N0} ms  ({PerVar(coldResolve),6:N2} ms/var)");
        L($"  Payload throughput                        : {MB(totalBytes) / Math.Max(0.001, wall.Elapsed.TotalSeconds):N1} MB/s (wall)");
        L("");
        L("---- WARM (re-index, all-fresh skip path) ----");
        L($"Wall clock : {warm.Elapsed.TotalSeconds,8:N2} s   ({warm.Elapsed.TotalMilliseconds / Math.Max(1, varCount):N3} ms/var — enumerate+stat only)");
        L($"Skipped    : {warmSkipped:N0} vars (fresh, no re-inspect)");
        L("");
        L($"================ EXTRAPOLATION → {targetVars:N0} vars / {targetTb:N1} TB ================");
        L("(phases scale with var COUNT — ingest reads central directories + meta + one thumb,");
        L(" not full payloads, so total TB mostly affects the directory walk, not per-var cost.)");
        var totalPerVar = PerVar(coldDiscovery) + PerVar(coldIngest) + PerVar(coldRefresh) + PerVar(coldResolve);
        L($"  Est. discovery    : {Est(PerVar(coldDiscovery))}");
        L($"  Est. ingest       : {Est(PerVar(coldIngest))}");
        L($"  Est. refresh      : {Est(PerVar(coldRefresh))}");
        L($"  Est. resolve      : {Est(PerVar(coldResolve))}");
        L($"  Est. TOTAL (cold) : {Est(totalPerVar)}   ({totalPerVar:N2} ms/var × {targetVars:N0})");
        L($"  Est. re-index     : {TimeSpan.FromMilliseconds(warm.Elapsed.TotalMilliseconds / Math.Max(1, varCount) * targetVars).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)} (warm/skip)");
        L("====================================================================================");

        var reportPath = Path.Combine(Path.GetTempPath(), $"varvault-index-benchmark-{varCount}vars.txt");
        try { File.WriteAllText(reportPath, sb.ToString()); log.WriteLine($"\nReport written: {reportPath}"); } catch { }

        Assert.True(summary.Indexed > 0, "cold index should have indexed vars");
        Assert.True(coldIndexed > 0, "stream pipeline should report vars via metrics");
        var maxMemoryMb = long.TryParse(Environment.GetEnvironmentVariable("VARVAULT_BENCH_MAX_MEMORY_MB"), out var mm) ? mm : 4096;
        Assert.True(MB(memory.PeakPrivateBytes) < maxMemoryMb,
            $"peak private memory {MB(memory.PeakPrivateBytes):N0} MB exceeded {maxMemoryMb:N0} MB budget");
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0)
            return 0;
        var index = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private sealed class ProcessMemorySampler : IAsyncDisposable
    {
        private readonly Process _process = Process.GetCurrentProcess();
        private readonly CancellationTokenSource _stop = new();
        private readonly int[] _gcStart = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        private readonly long _allocatedStart = GC.GetTotalAllocatedBytes();
        private readonly Task _sampling;
        private bool _stopped;

        public ProcessMemorySampler() => _sampling = Task.Run(SampleAsync);

        public long PeakWorkingSetBytes { get; private set; }
        public long PeakPrivateBytes { get; private set; }
        public long PeakManagedHeapBytes { get; private set; }
        public long PeakLohBytes { get; private set; }
        public long RetainedManagedHeapBytes { get; private set; }
        public long TotalAllocatedBytes { get; private set; }
        public int Gen0Collections => GC.CollectionCount(0) - _gcStart[0];
        public int Gen1Collections => GC.CollectionCount(1) - _gcStart[1];
        public int Gen2Collections => GC.CollectionCount(2) - _gcStart[2];

        private async Task SampleAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                _process.Refresh();
                PeakWorkingSetBytes = Math.Max(PeakWorkingSetBytes, _process.WorkingSet64);
                PeakPrivateBytes = Math.Max(PeakPrivateBytes, _process.PrivateMemorySize64);
                PeakManagedHeapBytes = Math.Max(PeakManagedHeapBytes, GC.GetTotalMemory(false));
                var generations = GC.GetGCMemoryInfo().GenerationInfo;
                if (generations.Length > 3)
                    PeakLohBytes = Math.Max(PeakLohBytes, generations[3].SizeAfterBytes);
                try { await Task.Delay(50, _stop.Token); }
                catch (OperationCanceledException) { break; }
            }
        }

        public async Task StopAsync()
        {
            if (_stopped)
                return;
            _stopped = true;
            await _stop.CancelAsync();
            await _sampling;
            TotalAllocatedBytes = GC.GetTotalAllocatedBytes() - _allocatedStart;
            RetainedManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: true);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _stop.Dispose();
            _process.Dispose();
        }
    }
}
