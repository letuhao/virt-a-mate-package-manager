using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VarVault.Common.Diagnostics;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Domain.ValueObjects;
using VarVault.Sdk.Indexing;

namespace VarVault.Modules.Indexing;

/// <summary>
/// The staged, incremental indexing orchestrator. Enumerates a repository, skips unchanged vars by
/// (size,mtime), inspects new/changed ones (identity + meta + signatures + classification + encoding),
/// upserts catalog rows through <see cref="ICatalogStore"/>, prunes vanished files (online repos only),
/// and refreshes the read model. Binds to no EF/Infrastructure type — only Domain/SDK seams. (IDX-1/2.)
/// </summary>
internal sealed class IndexingService(
    IServiceScopeFactory scopeFactory,
    IRepositoryEnumerator enumerator,
    IVarInspector inspector,
    ILogger<IndexingService> logger) : IIndexingService
{
    /// <summary>Vars per write transaction. Big enough to amortize commit cost, small enough to bound the WAL.</summary>
    private const int BatchSize = 512;
    /// <summary>Emit a throughput log line every this many indexed vars.</summary>
    private const int ProgressEvery = 5_000;
    /// <summary>Concurrent inspections. Inspection is CPU-bound (decompress), so scale to cores.</summary>
    private static readonly int Dop = Math.Max(2, Environment.ProcessorCount);

    /// <summary>One inspected var ready to persist, plus its lane flags. (Returned from the parallel inspect stage.)</summary>
    private readonly record struct InspectResult(VarUpsert Upsert, bool Unrecognized, bool Corrupt);

    public async Task<IndexResult> IndexRepositoryAsync(
        Guid repositoryId,
        string repositoryMountPath,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartActivity("index.repository");
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICatalogStore>();
        var previews = scope.ServiceProvider.GetRequiredService<IPreviewIndexer>();

        var online = await store.RepositoryIsOnlineAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        var existing = (await store.ListVarFilesAsync(repositoryId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(v => v.RelativePath, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirty = new ReadModelDirtySet(); // base-table writes mark packages; drained into one refresh (0.26)
        int indexed = 0, skipped = 0, corrupt = 0, unrecognized = 0;

        // Pass-0: enumerate (cheap, metadata only) → the set we've seen (for pruning) + the changed subset that
        // actually needs a (costly) inspection. Freshness skip avoids opening unchanged files. (IDX-2)
        var toInspect = new List<ScannedVar>();
        foreach (var scanned in enumerator.Enumerate(repositoryMountPath, includeQuarantined: true, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            seen.Add(scanned.RelativePath);
            if (existing.TryGetValue(scanned.RelativePath, out var ex) &&
                RepositoryScanRules.IsFresh(ex.SizeBytes, ex.FileMtimeUtc, scanned.SizeBytes, scanned.FileMtimeUtc))
                skipped++;
            else
                toInspect.Add(scanned);
        }

        // Inspection (open zip + decompress scene/preset JSON for refs + classify) is CPU-heavy and stateless per
        // var, so run a chunk of it across all cores, then write that chunk through the single-writer batch. The
        // dominant phase parallelizes ~Ncores× while catalog writes stay serialized + transactional. (perf.)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double inspectMs = 0, writeMs = 0;   // split so we know I/O-bound inspect vs serial DB write
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Dop, CancellationToken = cancellationToken };
        foreach (var chunk in toInspect.Chunk(BatchSize))
        {
            var results = new InspectResult?[chunk.Length];
            // Synchronous Parallel.For is the right primitive for CPU-bound work (real partitioning + work-stealing
            // across cores); ForEachAsync with a sync body under-parallelizes. Run it off the async caller's thread.
            var isw = System.Diagnostics.Stopwatch.StartNew();
            await Task.Run(() => Parallel.For(0, chunk.Length, parallel, i =>
            {
                var scanned = chunk[i];
                var inspection = inspector.Inspect(scanned.FullPath, parallel.CancellationToken);
                if (inspection.IsFailure)
                    logger.LogWarning("Skipping unreadable var {Path}: {Error}", scanned.RelativePath, inspection.Error);
                else
                    results[i] = BuildUpsert(repositoryId, scanned, inspection.Value); // distinct index → no race
            }), cancellationToken).ConfigureAwait(false);
            inspectMs += isw.Elapsed.TotalMilliseconds;

            var batch = new List<VarUpsert>(chunk.Length);
            foreach (var r in results)
            {
                if (r is not { } res)
                    continue;
                batch.Add(res.Upsert);
                if (res.Unrecognized) unrecognized++;
                if (res.Corrupt) corrupt++;
            }

            var wsw = System.Diagnostics.Stopwatch.StartNew();
            var ids = await store.ApplyBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            writeMs += wsw.Elapsed.TotalMilliseconds;
            foreach (var id in ids)
                dirty.MarkPackage(id);
            indexed += batch.Count;
            if (indexed >= ProgressEvery && indexed % ProgressEvery < BatchSize)
                logger.LogInformation("Indexing {Repo}: {Indexed}/{Total} vars ({Rate:F0}/s, {Dop} threads)",
                    repositoryId, indexed, toInspect.Count, indexed / Math.Max(0.001, sw.Elapsed.TotalSeconds), Dop);
        }

        logger.LogInformation("Index split for {Repo}: inspect {Inspect:F0} ms ({InspectPerVar:F1} ms/var, {Dop} threads), write {Write:F0} ms ({WritePerVar:F1} ms/var)",
            repositoryId, inspectMs, indexed > 0 ? inspectMs / indexed : 0, Dop, writeMs, indexed > 0 ? writeMs / indexed : 0);
        Telemetry.IndexInspectDurationMs.Record(inspectMs);
        Telemetry.IndexWriteDurationMs.Record(writeMs);
        Telemetry.IndexVarsIndexed.Add(indexed);
        Telemetry.IndexScanDurationMs.Record(sw.Elapsed.TotalMilliseconds);

        // Prune vanished files — only when the repository is confirmed online (offline ≠ gone). (1.28 ⚠)
        var pruned = 0;
        if (online)
        {
            var vanished = existing.Where(kv => !seen.Contains(kv.Key)).Select(kv => kv.Value).ToList();
            if (vanished.Count > 0)
            {
                foreach (var v in vanished)
                    dirty.MarkPackage(v.PackageId);
                pruned = await store.RemoveVarFilesAsync(vanished.Select(v => v.Id).ToList(), cancellationToken).ConfigureAwait(false);
            }
        }

        // Pass-1 complete: refresh the read model so the catalog is browsable (names/meta/deps/counts). (1.23)
        var affected = dirty.Drain();
        if (affected.Count > 0)
        {
            var rsw = System.Diagnostics.Stopwatch.StartNew();
            await store.RefreshReadModelAsync(affected, cancellationToken).ConfigureAwait(false);
            Telemetry.IndexRefreshDurationMs.Record(rsw.Elapsed.TotalMilliseconds);
        }

        // Pass-2: previews/thumbnails fill in afterwards — the gallery already shows type placeholders. (1.23/1.32)
        if (affected.Count > 0)
            await previews.BuildPreviewsAsync(affected, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Indexed repository {RepositoryId}: {Indexed} indexed, {Skipped} skipped, {Pruned} pruned, {Corrupt} corrupt, {Unrecognized} unrecognized",
            repositoryId, indexed, skipped, pruned, corrupt, unrecognized);

        return new IndexResult(indexed, skipped, pruned, corrupt, unrecognized);
    }

    /// <summary>Pure — safe to call concurrently from the parallel inspect stage.</summary>
    private static InspectResult BuildUpsert(Guid repositoryId, ScannedVar scanned, VarInspection inspection)
    {
        var fileName = System.IO.Path.GetFileName(scanned.RelativePath);
        var parse = PackageId.TryParse(fileName);
        var identity = parse.IsSuccess ? parse.Value : null;
        var unrecognized = identity is null;
        var corrupt = inspection.Integrity == IntegrityStatus.CorruptZip;

        // BadName overrides only when the zip itself is otherwise fine.
        var integrity = inspection.Integrity;
        if (identity is null && integrity == IntegrityStatus.Ok)
            integrity = IntegrityStatus.BadName;

        var meta = inspection.Meta;
        var encoding = inspection.Encoding;
        var signatures = inspection.Signatures;

        var gender = GenderInference.Infer(inspection.Entries.Select(e => e.DecodedNameBestEffort));
        var items = inspection.Classification?.Items ?? [];
        var contentItems = items
            .Select(i => new UpsertContentItem(i.Type, i.EntryPath, i.IsPreset,
                gender.Gender == Gender.Auto ? null : gender.Gender,
                gender.Gender == Gender.Auto ? null : gender.Confidence))
            .ToList();

        var counts = inspection.Classification?.Counts ?? new Dictionary<ContentType, int>();

        var upsert = new VarUpsert(
            RepositoryId: repositoryId,
            RelativePath: scanned.RelativePath,
            SizeBytes: scanned.SizeBytes,
            FileMtimeUtc: scanned.FileMtimeUtc,
            Quarantine: scanned.Quarantine,
            Identity: identity,
            Integrity: integrity,
            MetaCreator: meta?.Creator,
            MetaPackage: meta?.Package,
            LicenseType: meta?.LicenseType,
            Description: meta?.Description,
            ProgramVersion: meta?.ProgramVersion,
            ContentSignature: signatures?.ContentSignature,
            PayloadSignature: signatures?.PayloadSignature,
            ContentSignatureNoPath: signatures?.ContentSignatureNoPath,
            EncodingHealth: encoding?.Health ?? EncodingHealth.Unknown,
            DetectedCodepage: encoding?.DetectedCodepage,
            BrokenEntryCount: encoding?.BrokenEntryCount ?? 0,
            ContentItems: contentItems,
            ContentCounts: counts,
            DependencyRefsRaw: meta?.DependencyRefs ?? [],
            EmbeddedRefsRaw: inspection.EmbeddedRefs);

        return new InspectResult(upsert, unrecognized, corrupt);
    }
}
