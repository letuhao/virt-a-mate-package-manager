using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VarVault.Common;
using VarVault.Common.Diagnostics;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Domain.ValueObjects;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Bounded raw-first indexer: stream discovery → per-drive claim/inspect → persist → durable dirty
/// → paged resolve/refresh. Used by the indexer worker. (A12–A15.)
/// </summary>
public sealed class StreamIndexer(
    IServiceScopeFactory scopeFactory,
    IRepositoryEnumerator enumerator,
    OneHandleVarInspector inspector,
    IWriteQueue writeQueue,
    ILogger<StreamIndexer> logger) : IStreamIndexer
{
    public async Task<IndexOutcome> IndexRepositoryAsync(
        Guid repositoryId,
        string mountPath,
        MediaType mediaType,
        IProgressSink? progress = null,
        bool forceFull = false,
        CancellationToken cancellationToken = default)
    {
        progress ??= IProgressSink.Null;
        var leaseOwner = Guid.NewGuid();
        var dop = Math.Max(1, IngestLimits.DegreeFor(mediaType));

        using var activity = Telemetry.StartActivity("index.stream");
        activity?.SetTag("repository.id", repositoryId);
        activity?.SetTag("media.type", mediaType.ToString());
        activity?.SetTag("ingest.dop", dop);
        var total = Stopwatch.StartNew();
        logger.LogInformation(
            "Stream index starting for repository {RepositoryId} at {MountPath} (media {MediaType}, DOP {Dop})",
            repositoryId, mountPath, mediaType, dop);

        using var scope = scopeFactory.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<IScanLedger>();
        var store = scope.ServiceProvider.GetRequiredService<ICatalogStore>();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();

        // Fast-path: unless forced, a cheap metadata-only walk builds the repository signature; if it
        // matches the last completed scan AND no var is mid-ingest, nothing changed → skip the whole run
        // (no new generation, no per-file re-stamp, no prune). (A16.)
        // Defense: if the catalog still has more VarFiles than disk reports, a prior run likely crashed
        // before prune — fall through so orphans are removed.
        if (!forceFull)
        {
            var previous = await ledger.GetLastCompletedSignatureAsync(repositoryId, cancellationToken).ConfigureAwait(false);
            if (previous is { } prevSig)
            {
                progress.Report(new ProgressReport(0, 0, "Checking for changes…"));
                var current = ComputeSignature(mountPath, cancellationToken);
                var hasPending = await ledger.HasPendingWorkAsync(repositoryId, cancellationToken).ConfigureAwait(false);
                if (!hasPending && current == prevSig)
                {
                    var catalogCount = await db.VarFiles.AsNoTracking()
                        .CountAsync(v => v.RepositoryId == repositoryId, cancellationToken)
                        .ConfigureAwait(false);
                    if (catalogCount <= current.FileCount)
                    {
                        total.Stop();
                        Telemetry.IndexScanDurationMs.Record(total.Elapsed.TotalMilliseconds);
                        logger.LogInformation(
                            "Stream index skipped for repository {RepositoryId}: unchanged ({FileCount} files) — " +
                            "checked in {ElapsedMs} ms",
                            repositoryId, current.FileCount, total.ElapsedMilliseconds);
                        progress.Report(new ProgressReport(current.FileCount, Math.Max(1, current.FileCount), "Up to date — no changes"));
                        return new IndexOutcome(0, (int)current.FileCount, 0, 0, 0);
                    }

                    logger.LogInformation(
                        "Stream index forcing full scan for repository {RepositoryId}: catalog has {CatalogCount} " +
                        "rows but disk signature reports {FileCount} files (likely unfinished prune)",
                        repositoryId, catalogCount, current.FileCount);
                }
            }
        }

        var run = await writeQueue.EnqueueScopedAsync(
            (sp, ct) => sp.GetRequiredService<IScanLedger>().BeginRunAsync(repositoryId, ct), WritePriority.Bulk, cancellationToken).ConfigureAwait(false);

        progress.Report(new ProgressReport(0, 0, "Discovering…"));
        await writeQueue.EnqueueScopedAsync((sp, ct) => sp.GetRequiredService<IScanLedger>().SetPhaseAsync(run.Id, ScanPhase.Discovering, ct), WritePriority.Bulk, cancellationToken).ConfigureAwait(false);

        var discoverySw = Stopwatch.StartNew();
        long discovered = 0;
        var signature = RepositorySignature.Empty;
        var discoveryBatch = new List<ScannedVar>(IngestLimits.DiscoveryBatchSize);
        long discoveryBatchBytes = 0;
        foreach (var scanned in enumerator.Enumerate(mountPath, includeQuarantined: true, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            discoveryBatch.Add(scanned);
            signature = signature.Add(scanned.SizeBytes, scanned.FileMtimeUtc, scanned.RelativePath);
            discoveryBatchBytes += scanned.RelativePath.Length * 2L + 128;
            discovered++;
            if (discoveryBatch.Count >= IngestLimits.DiscoveryBatchSize ||
                discoveryBatchBytes >= IngestLimits.MaxDiscoveryBatchBytes)
            {
                await writeQueue.EnqueueScopedAsync(
                    (sp, ct) => sp.GetRequiredService<IScanLedger>().UpsertDiscoveryBatchAsync(run.Id, repositoryId, discoveryBatch, ct),
                    WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
                discoveryBatch.Clear();
                discoveryBatchBytes = 0;
            }
            if (discovered % 2000 == 0)
                progress.Report(new ProgressReport(discovered, 0, $"Discovering — {discovered:N0} files"));
        }
        if (discoveryBatch.Count > 0)
        {
            await writeQueue.EnqueueScopedAsync(
                (sp, ct) => sp.GetRequiredService<IScanLedger>().UpsertDiscoveryBatchAsync(run.Id, repositoryId, discoveryBatch, ct),
                WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
        }
        discoverySw.Stop();
        Telemetry.IndexDiscoveryDurationMs.Record(discoverySw.Elapsed.TotalMilliseconds);
        // Record the fingerprint now (trusted only once the run completes) so the next auto-index can
        // skip an unchanged repository. (A16.)
        await writeQueue.EnqueueScopedAsync(
            (sp, ct) => sp.GetRequiredService<IScanLedger>().SetSignatureAsync(run.Id, signature, ct), WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Discovery for run {RunId} (gen {Generation}) found {Discovered} files in {ElapsedMs} ms",
            run.Id, run.Generation, discovered, discoverySw.ElapsedMilliseconds);

        await writeQueue.EnqueueScopedAsync((sp, ct) => sp.GetRequiredService<IScanLedger>().SetPhaseAsync(run.Id, ScanPhase.Ingesting, ct), WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
        progress.Report(new ProgressReport(0, discovered, "Ingesting…"));
        var ingestSw = Stopwatch.StartNew();

        int indexed = 0, skipped = 0, pruned = 0, corrupt = 0, unrecognized = 0;
        var channel = Channel.CreateBounded<long>(new BoundedChannelOptions(IngestLimits.DefaultPipelineCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false,
        });
        // Inspected-but-not-persisted vars; small capacity + batch byte budget = the pipeline RAM ceiling.
        var pendingChannel = Channel.CreateBounded<PendingVar>(new BoundedChannelOptions(IngestLimits.PendingChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
        });

        var producers = Task.Run(async () =>
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var batch = await writeQueue.EnqueueScopedAsync(
                        (sp, ct) => sp.GetRequiredService<IScanLedger>().ClaimWorkAsync(run.Id, repositoryId, leaseOwner, IngestLimits.PersistBatchSize, ct),
                        WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
                    if (batch.Count == 0)
                        break;
                    foreach (var id in batch)
                        await channel.Writer.WriteAsync(id, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, cancellationToken);

        var workers = Enumerable.Range(0, dop).Select(_ => Task.Run(async () =>
        {
            await foreach (var varFileId in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var pending = await InspectOneAsync(varFileId, repositoryId, mountPath, cancellationToken).ConfigureAwait(false);
                    await pendingChannel.Writer.WriteAsync(pending, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Inspect failed for VarFile {Id}", varFileId);
                    await writeQueue.EnqueueScopedAsync(
                        (sp, ct) => sp.GetRequiredService<IScanLedger>().MarkFailedAsync(varFileId, ex.Message, ct), WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref corrupt);
                }
            }
        }, cancellationToken)).ToArray();

        // Single persister: drain inspected vars and flush in batches — one write action + one thumb-ref
        // action per batch instead of three actions per var. Flush on count N or the byte budget,
        // whichever comes first, so buffered RAM is capped regardless of N.
        var persister = Task.Run(async () =>
        {
            var batch = new List<PendingVar>(IngestLimits.PersistBatchSize);
            long batchBytes = 0;
            await foreach (var pending in pendingChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                batch.Add(pending);
                batchBytes += pending.EstimatedBytes;
                if (batch.Count >= IngestLimits.PersistBatchSize || batchBytes >= IngestLimits.MaxPersistBatchBytes)
                {
                    var (ok, failed) = await FlushBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                    Interlocked.Add(ref indexed, ok);
                    Interlocked.Add(ref corrupt, failed);
                    batch.Clear();
                    batchBytes = 0;
                    var done = Volatile.Read(ref indexed) + Volatile.Read(ref corrupt);
                    progress.Report(new ProgressReport(done, Math.Max(discovered, done), "Ingesting…"));
                }
            }
            if (batch.Count > 0)
            {
                var (ok, failed) = await FlushBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                Interlocked.Add(ref indexed, ok);
                Interlocked.Add(ref corrupt, failed);
            }
        }, cancellationToken);

        await producers.ConfigureAwait(false);
        await Task.WhenAll(workers).ConfigureAwait(false);
        pendingChannel.Writer.TryComplete();
        await persister.ConfigureAwait(false);
        ingestSw.Stop();
        Telemetry.IndexIngestDurationMs.Record(ingestSw.Elapsed.TotalMilliseconds);
        Telemetry.IndexVarsIndexed.Add(indexed);
        if (corrupt > 0)
            Telemetry.IndexVarsFailed.Add(corrupt);
        logger.LogInformation(
            "Ingest for run {RunId} finished: {Indexed} indexed, {Corrupt} failed in {ElapsedMs} ms ({PerVarMs:F2} ms/var)",
            run.Id, indexed, corrupt, ingestSw.ElapsedMilliseconds,
            indexed > 0 ? ingestSw.Elapsed.TotalMilliseconds / indexed : 0);

        var online = await store.RepositoryIsOnlineAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        if (online)
        {
            var gen = run.Generation;
            // Page deletes — never materialize every vanished id for a multi-TB repo.
            const int vanishPage = 500;
            try
            {
                while (true)
                {
                    var vanishedIds = await db.VarFiles.AsNoTracking()
                        .Where(v => v.RepositoryId == repositoryId && v.SeenGeneration != gen)
                        .OrderBy(v => v.Id)
                        .Take(vanishPage)
                        .Select(v => v.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (vanishedIds.Count == 0)
                        break;
                    pruned += await writeQueue.EnqueueScopedAsync(
                        (sp, ct) => sp.GetRequiredService<ICatalogStore>().RemoveVarFilesAsync(vanishedIds, ct), WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Prune must not abort Library refresh — otherwise Exact-visible packages stay invisible.
                logger.LogError(ex,
                    "Prune vanished vars failed for repository {RepositoryId} after gen {Generation}; continuing to refresh",
                    repositoryId, gen);
            }
        }

        // Heal Library gaps before refresh: Package+VarFile may exist (Exact finds them) while
        // PackageListItem was never written (dirty drained before refresh). Re-dirty those packages.
        var orphanPackageIds = await db.VarFiles.AsNoTracking()
            .Where(v => v.RepositoryId == repositoryId && v.PackageId != null)
            .Select(v => v.PackageId!.Value)
            .Distinct()
            .Where(pid => !db.PackageListItems.Any(i => i.PackageId == pid))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (orphanPackageIds.Count > 0)
        {
            logger.LogInformation(
                "Healing {Count} packages missing Library rows for repository {RepositoryId}",
                orphanPackageIds.Count, repositoryId);
            await writeQueue.EnqueueScopedAsync(
                (sp, ct) => sp.GetRequiredService<IDurableDirtySet>().MarkManyAsync(orphanPackageIds, "heal-missing-list", ct),
                WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
        }

        await writeQueue.EnqueueScopedAsync((sp, ct) => sp.GetRequiredService<IScanLedger>().SetPhaseAsync(run.Id, ScanPhase.Refreshing, ct), WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
        progress.Report(new ProgressReport(discovered, discovered, "Refreshing catalog…"));
        var refreshSw = Stopwatch.StartNew();
        while (true)
        {
            // Peek → refresh → ack (never delete dirty before refresh commits). A crash between
            // drain-and-refresh used to leave Exact-visible Package/VarFile with no Library row.
            var dirtyBatch = await writeQueue.EnqueueScopedAsync(
                (sp, ct) => sp.GetRequiredService<IDurableDirtySet>().PeekBatchAsync(256, ct), WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
            if (dirtyBatch.Count == 0)
                break;
            await writeQueue.EnqueueScopedAsync(
                (sp, ct) => sp.GetRequiredService<ICatalogStore>().RefreshReadModelAsync(dirtyBatch, ct), WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
            await writeQueue.EnqueueScopedAsync(
                (sp, ct) => sp.GetRequiredService<IDurableDirtySet>().AcknowledgeAsync(dirtyBatch, ct), WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
        }
        refreshSw.Stop();
        Telemetry.IndexRefreshDurationMs.Record(refreshSw.Elapsed.TotalMilliseconds);

        await writeQueue.EnqueueScopedAsync((sp, ct) => sp.GetRequiredService<IScanLedger>().CheckpointWalAsync(ct), WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
        await writeQueue.EnqueueScopedAsync((sp, ct) => sp.GetRequiredService<IScanLedger>().AnalyzeAsync(ct), WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
        await writeQueue.EnqueueScopedAsync(
            (sp, ct) => sp.GetRequiredService<IScanLedger>().CompleteAsync(run.Id, ScanPhase.Completed, null, ct), WritePriority.Bulk, cancellationToken).ConfigureAwait(false);

        var final = await ledger.GetRunAsync(run.Id, cancellationToken).ConfigureAwait(false);
        skipped = (int)(final?.Skipped ?? 0);
        if (skipped > 0)
            Telemetry.IndexVarsSkipped.Add(skipped);
        total.Stop();
        Telemetry.IndexScanDurationMs.Record(total.Elapsed.TotalMilliseconds);
        activity?.SetTag("index.indexed", indexed);
        activity?.SetTag("index.skipped", skipped);
        activity?.SetTag("index.pruned", pruned);
        activity?.SetTag("index.corrupt", corrupt);
        logger.LogInformation(
            "Stream index for repository {RepositoryId} completed: {Indexed} indexed, {Skipped} skipped, " +
            "{Pruned} pruned, {Corrupt} corrupt — discovery {DiscoveryMs} ms, ingest {IngestMs} ms, " +
            "refresh {RefreshMs} ms, total {TotalMs} ms",
            repositoryId, indexed, skipped, pruned, corrupt,
            discoverySw.ElapsedMilliseconds, ingestSw.ElapsedMilliseconds,
            refreshSw.ElapsedMilliseconds, total.ElapsedMilliseconds);
        return new IndexOutcome(indexed, skipped, pruned, corrupt, unrecognized);
    }

    /// <summary>Metadata-only walk building the repository fingerprint — no file opens, no DB writes. (A16.)</summary>
    private RepositorySignature ComputeSignature(string mountPath, CancellationToken cancellationToken)
    {
        var sig = RepositorySignature.Empty;
        foreach (var scanned in enumerator.Enumerate(mountPath, includeQuarantined: true, cancellationToken))
            sig = sig.Add(scanned.SizeBytes, scanned.FileMtimeUtc, scanned.RelativePath);
        return sig;
    }

    /// <summary>An inspected var waiting for the batched persist. Holds only the compact upsert + one thumb.</summary>
    private sealed record PendingVar(long VarFileId, VarUpsert Upsert, byte[]? ThumbJpeg, long EstimatedBytes);

    /// <summary>Read facts + one-handle inspect; no catalog writes (those happen in the batch flush).</summary>
    private async Task<PendingVar> InspectOneAsync(
        long varFileId,
        Guid repositoryId,
        string mountPath,
        CancellationToken cancellationToken)
    {
        string relativePath;
        long sizeBytes;
        DateTime mtime;
        QuarantineKind quarantine;
        using (var readScope = scopeFactory.CreateScope())
        {
            var db = readScope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            var vf = await db.VarFiles.AsNoTracking().FirstAsync(v => v.Id == varFileId, cancellationToken).ConfigureAwait(false);
            relativePath = vf.RelativePath;
            sizeBytes = vf.SizeBytes;
            mtime = vf.FileMtime;
            quarantine = vf.QuarantineKind;
        }

        var fullPath = Path.Combine(mountPath, relativePath);
        var inspectSw = Stopwatch.StartNew();
        var result = inspector.Inspect(fullPath, cancellationToken);
        inspectSw.Stop();
        Telemetry.IndexInspectDurationMs.Record(inspectSw.Elapsed.TotalMilliseconds);
        var scanned = new ScannedVar(fullPath, relativePath, sizeBytes, mtime, quarantine);
        var upsert = BuildUpsert(repositoryId, scanned, result.Inspection);
        return new PendingVar(varFileId, upsert, result.RepresentativeThumbJpeg, EstimateBytes(upsert, result.RepresentativeThumbJpeg));
    }

    /// <summary>
    /// Persist a batch: ONE write action applies all upserts + raw-stored + dirty marks (one scope, one
    /// cross-process lock acquire), thumbnails go to the sharded store outside the catalog lock, then one
    /// small action stamps the thumb refs. Per-item failures mark that var Failed without sinking the batch.
    /// </summary>
    private async Task<(int Ok, int Failed)> FlushBatchAsync(List<PendingVar> batch, CancellationToken cancellationToken)
    {
        var estimatedBytes = batch.Sum(p => p.EstimatedBytes);
        Telemetry.IndexPersistBatchItems.Record(batch.Count);
        Telemetry.IndexPersistBatchBytes.Record(estimatedBytes);
        var writeSw = Stopwatch.StartNew();
        var applied = await writeQueue.EnqueueAsync(async ct =>
        {
            var results = new List<(PendingVar Pending, long? PackageId, bool Ok)>(batch.Count);
            try
            {
                // Fast path: all catalog upserts share one SQLite transaction. ApplyBatchAsync clears
                // EF tracking per var, so memory remains O(rows in one var), not O(batch rows).
                using var batchScope = scopeFactory.CreateScope();
                var store = batchScope.ServiceProvider.GetRequiredService<ICatalogStore>();
                var packageIds = await store.ApplyBatchAsync(batch.Select(p => p.Upsert).ToList(), ct)
                    .ConfigureAwait(false);
                for (var i = 0; i < batch.Count; i++)
                    results.Add((batch[i], packageIds[i], true));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A malformed item must not roll back every good var. Retry individually in fresh scopes
                // after the batch transaction has rolled back; this path is intentionally slower.
                logger.LogWarning(ex, "Batch persist failed for {Count} vars; isolating individual failures", batch.Count);
                results.Clear();
                foreach (var pending in batch)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var itemScope = scopeFactory.CreateScope();
                        var packageId = await itemScope.ServiceProvider.GetRequiredService<ICatalogStore>()
                            .ApplyAsync(pending.Upsert, ct).ConfigureAwait(false);
                        results.Add((pending, packageId, true));
                    }
                    catch (Exception itemEx) when (itemEx is not OperationCanceledException)
                    {
                        logger.LogWarning(itemEx, "Persist failed for VarFile {Id}", pending.VarFileId);
                        results.Add((pending, null, false));
                    }
                }
            }

            using var stateScope = scopeFactory.CreateScope();
            var ledger = stateScope.ServiceProvider.GetRequiredService<IScanLedger>();
            var dirty = stateScope.ServiceProvider.GetRequiredService<IDurableDirtySet>();
            foreach (var (pending, packageId, ok) in results)
            {
                if (ok)
                {
                    await ledger.MarkRawStoredAsync(pending.VarFileId, ct).ConfigureAwait(false);
                    if (packageId is { } dirtyId)
                        await dirty.MarkAsync(dirtyId, "ingest", ct).ConfigureAwait(false);
                }
                else
                {
                    await ledger.MarkFailedAsync(pending.VarFileId, "catalog persist failed", ct).ConfigureAwait(false);
                }
            }
            return results;
        }, WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
        writeSw.Stop();
        Telemetry.IndexWriteDurationMs.Record(writeSw.Elapsed.TotalMilliseconds);

        // Thumbnails: sharded stores are separate DBs — write them outside the catalog writer lock.
        var thumbed = new List<long>();
        foreach (var (pending, packageId, ok) in applied)
        {
            if (!ok || packageId is not { } pid || pending.ThumbJpeg is not { Length: > 0 } jpeg)
                continue;
            using var thumbScope = scopeFactory.CreateScope();
            await thumbScope.ServiceProvider.GetRequiredService<IThumbnailStore>()
                .PutAsync(pid, jpeg, cancellationToken).ConfigureAwait(false);
            Telemetry.PreviewsExtracted.Add(1);
            Telemetry.PreviewBytesStored.Record(jpeg.Length);
            thumbed.Add(pid);
        }

        if (thumbed.Count > 0)
        {
            await writeQueue.EnqueueScopedAsync(async (sp, ct) =>
            {
                var scopedDb = sp.GetRequiredService<VarVaultDbContext>();
                foreach (var pid in thumbed)
                {
                    var thumbRef = $"thumb:{pid}";
                    await scopedDb.PackageListItems.Where(x => x.PackageId == pid)
                        .ExecuteUpdateAsync(u => u.SetProperty(x => x.PreviewThumbRef, thumbRef), ct)
                        .ConfigureAwait(false);
                }
                return true;
            }, WritePriority.Bulk, cancellationToken).ConfigureAwait(false);
        }

        var okCount = applied.Count(r => r.Ok);
        return (okCount, applied.Count - okCount);
    }

    /// <summary>Rough per-item RAM estimate used for the batch byte budget (thumb dominates).</summary>
    private static long EstimateBytes(VarUpsert upsert, byte[]? thumb) =>
        (thumb?.LongLength ?? 0)
        + upsert.ContentItems.Count * 256L
        + (upsert.DependencyRefsRaw.Count + upsert.EmbeddedRefsRaw.Count) * 128L
        + (upsert.Description?.Length ?? 0) * 2L
        + 4096;

    private static VarUpsert BuildUpsert(Guid repositoryId, ScannedVar scanned, VarInspection inspection)
    {
        var fileName = Path.GetFileName(scanned.RelativePath);
        var parse = PackageId.TryParse(fileName);
        var identity = parse.IsSuccess ? parse.Value : null;
        var integrity = inspection.Integrity;
        if (identity is null && integrity == IntegrityStatus.Ok)
            integrity = IntegrityStatus.BadName;

        var gender = GenderInference.Infer(inspection.Entries.Select(e => e.DecodedNameBestEffort));
        var items = inspection.Classification?.Items ?? [];
        var contentItems = items
            .Select(i => new UpsertContentItem(i.Type, i.EntryPath, i.IsPreset,
                gender.Gender == Gender.Auto ? null : gender.Gender,
                gender.Gender == Gender.Auto ? null : gender.Confidence))
            .ToList();
        var counts = inspection.Classification?.Counts ?? new Dictionary<ContentType, int>();

        return new VarUpsert(
            RepositoryId: repositoryId,
            RelativePath: scanned.RelativePath,
            SizeBytes: scanned.SizeBytes,
            FileMtimeUtc: scanned.FileMtimeUtc,
            Quarantine: scanned.Quarantine,
            Identity: identity,
            Integrity: integrity,
            MetaCreator: inspection.Meta?.Creator,
            MetaPackage: inspection.Meta?.Package,
            LicenseType: inspection.Meta?.LicenseType,
            Description: inspection.Meta?.Description,
            ProgramVersion: inspection.Meta?.ProgramVersion,
            ContentSignature: inspection.Signatures?.ContentSignature,
            PayloadSignature: inspection.Signatures?.PayloadSignature,
            ContentSignatureNoPath: inspection.Signatures?.ContentSignatureNoPath,
            EncodingHealth: inspection.Encoding?.Health ?? EncodingHealth.Unknown,
            DetectedCodepage: inspection.Encoding?.DetectedCodepage,
            BrokenEntryCount: inspection.Encoding?.BrokenEntryCount ?? 0,
            ContentItems: contentItems,
            ContentCounts: counts,
            DependencyRefsRaw: inspection.Meta?.DependencyRefs ?? [],
            EmbeddedRefsRaw: inspection.EmbeddedRefs);
    }
}
