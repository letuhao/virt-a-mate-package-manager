using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VarVault.Common;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexer;
using VarVault.Sdk.Repositories;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Coalescing indexer worker: one active job at a time, durable stream ingest, then paged resolve.
/// Hosted in-process (tests) or inside <c>VarVault.Indexer</c>. (A12.)
/// </summary>
public sealed class IndexerWorker(
    IServiceScopeFactory scopeFactory,
    IWriteQueue writeQueue,
    ILogger<IndexerWorker> logger) : IIndexerWorker
{
    private readonly object _gate = new();
    private readonly HashSet<int> _owners = [];
    private IndexerStatus _status = Idle();
    private CancellationTokenSource? _jobCts;
    private Task? _running;
    private Guid? _activeJob;
    /// <summary>
    /// One deferred start when a second Start* arrives while busy. Dropping those requests made
    /// Import.Apply "succeed" on a pre-copy IndexAll — files on disk, no PackageListItem.
    /// </summary>
    private PendingStart? _pending;

    public IndexerStatus Snapshot
    {
        get { lock (_gate) return _status; }
    }

    public bool IsBusy
    {
        get { lock (_gate) return _running is { IsCompleted: false }; }
    }

    public bool HasLiveOwner
    {
        get
        {
            lock (_gate)
            {
                _owners.RemoveWhere(pid => !ProcessLiveness.IsAlive(pid));
                return _owners.Count > 0;
            }
        }
    }

    public async Task<IndexerStatus> HandleAsync(IndexerCommand command, CancellationToken cancellationToken = default)
    {
        if (command.ProtocolVersion != IndexerProtocol.Version)
            return Snapshot with { Error = $"protocol mismatch: {command.ProtocolVersion}" };

        // Any command may carry the caller's pid; track it so the watchdog knows a GUI is attached.
        if (command.OwnerProcessId is { } ownerPid and > 0
            && command.Kind is not IndexerCommandKind.UnregisterOwner)
        {
            lock (_gate) _owners.Add(ownerPid);
        }

        switch (command.Kind)
        {
            case IndexerCommandKind.Ping:
            case IndexerCommandKind.GetStatus:
            case IndexerCommandKind.RegisterOwner:
                lock (_gate)
                    return _status with { QueueDepth = _pending is null ? 0 : 1 };

            case IndexerCommandKind.UnregisterOwner:
                if (command.OwnerProcessId is { } gone)
                    lock (_gate) _owners.Remove(gone);
                return Snapshot;

            case IndexerCommandKind.CancelJob:
                CancelActive(command.JobId);
                return Snapshot;

            case IndexerCommandKind.Shutdown:
                CancelActive(null);
                if (_running is not null)
                {
                    try { await _running.ConfigureAwait(false); } catch { /* swallow */ }
                }
                lock (_gate) { _pending = null; }
                Set(_ => Idle());
                return Snapshot;

            case IndexerCommandKind.StartIndexAll:
                return Start(null, command.ForceFull);

            case IndexerCommandKind.StartIndexRepository:
                if (command.RepositoryId is not { } rid)
                    return Snapshot with { Error = "repositoryId required" };
                return Start(rid, command.ForceFull);

            case IndexerCommandKind.ExtractContentPreview:
                if (command.ContentItemId is not long contentItemId || contentItemId <= 0)
                    return Snapshot with { Error = "contentItemId required" };
                return await ExtractContentPreviewAsync(contentItemId, focus: false, cancellationToken).ConfigureAwait(false);

            case IndexerCommandKind.ExtractContentFocusPreview:
                if (command.ContentItemId is not long focusId || focusId <= 0)
                    return Snapshot with { Error = "contentItemId required" };
                return await ExtractContentPreviewAsync(focusId, focus: true, cancellationToken).ConfigureAwait(false);

            default:
                return Snapshot with { Error = "unknown command" };
        }
    }

    private async Task<IndexerStatus> ExtractContentPreviewAsync(long contentItemId, bool focus, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            var thumbnails = scope.ServiceProvider.GetRequiredService<IThumbnailStore>();
            if (focus)
            {
                if (await thumbnails.GetFocusContentAsync(contentItemId, cancellationToken).ConfigureAwait(false) is not null)
                    return Snapshot with { Error = null };
            }
            else if (await thumbnails.GetContentAsync(contentItemId, cancellationToken).ConfigureAwait(false) is not null)
            {
                return Snapshot with { Error = null };
            }

            var item = await (
                from content in db.ContentItems.AsNoTracking()
                join varFile in db.VarFiles.AsNoTracking() on content.VarFileId equals varFile.Id
                join repository in db.Repositories.AsNoTracking() on varFile.RepositoryId equals repository.Id
                where content.Id == contentItemId
                select new
                {
                    content.EntryPath,
                    content.Type,
                    varFile.RelativePath,
                    repository.MountPath,
                    repository.IsOnline,
                })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (item is null)
                return Snapshot with { Error = "content item not found" };
            if (!item.IsOnline)
                return Snapshot with { Error = "content copy is offline" };
            if (!VarVault.Domain.Content.PreviewRules.HasPreview(item.Type))
                return Snapshot with { Error = "content type has no preview" };

            var extractor = scope.ServiceProvider.GetRequiredService<PreviewExtractor>();
            var maxDim = focus ? PreviewExtractor.FocusMaxDimension : PreviewExtractor.MaxDimension;
            var bytes = await extractor.ExtractAsync(
                Path.Combine(item.MountPath, item.RelativePath),
                item.EntryPath,
                maxDim,
                cancellationToken).ConfigureAwait(false);
            if (bytes is null)
                return Snapshot with { Error = "preview is missing or corrupt" };

            if (focus)
            {
                await thumbnails.PutFocusContentAsync(contentItemId, bytes, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await thumbnails.PutContentAsync(contentItemId, bytes, cancellationToken).ConfigureAwait(false);
                await writeQueue.EnqueueAsync(
                    ct => db.ContentItems.Where(c => c.Id == contentItemId)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(c => c.PreviewThumbRef, $"content-thumb:{contentItemId}"),
                            ct),
                    WritePriority.Interactive,
                    cancellationToken).ConfigureAwait(false);
            }
            return Snapshot with { Error = null };
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Content preview extraction failed for {ContentItemId} (focus={Focus})", contentItemId, focus);
            return Snapshot with { Error = ex.Message };
        }
    }

    private IndexerStatus Start(Guid? repositoryId, bool forceFull)
    {
        lock (_gate)
        {
            // Busy → queue one follow-up (merge scope/force). Return the *follow-up* job id so
            // Import.WaitForIndex attaches to the post-copy scan, not the already-running one.
            if (_running is { IsCompleted: false })
            {
                _pending = _pending is null
                    ? new PendingStart(repositoryId, forceFull, Guid.NewGuid())
                    : _pending.Merge(repositoryId, forceFull);
                logger.LogInformation(
                    "Indexer follow-up queued ({Scope}, forceFull={ForceFull}, job {JobId}) behind active {Active}",
                    _pending.RepositoryId is { } r ? $"repository {r}" : "all repositories",
                    _pending.ForceFull, _pending.JobId, _activeJob);
                return new IndexerStatus(
                    IndexerProtocol.Version, IndexerJobState.Discovering, _pending.JobId,
                    "Queued follow-up index", 0, 0, Process.GetCurrentProcess().WorkingSet64,
                    1, 0, _status.CatalogGeneration, null);
            }

            return BeginJob_NoLock(repositoryId, forceFull, Guid.NewGuid());
        }
    }

    /// <summary>Caller must hold <see cref="_gate"/> and the worker must not be busy.</summary>
    private IndexerStatus BeginJob_NoLock(Guid? repositoryId, bool forceFull, Guid jobId)
    {
        _activeJob = jobId;
        // Job lifetime is independent of the pipe/request CT (those end when the command returns).
        // Cancellation is only via CancelJob / Shutdown.
        _jobCts?.Dispose();
        _jobCts = new CancellationTokenSource();
        var ct = _jobCts.Token;
        _status = new IndexerStatus(
            IndexerProtocol.Version, IndexerJobState.Discovering, jobId,
            "Starting…", 0, 0, Process.GetCurrentProcess().WorkingSet64,
            _pending is null ? 0 : 1, 0, 0, null);
        _running = Task.Run(() => RunJobAsync(jobId, repositoryId, forceFull, ct), CancellationToken.None);
        return _status;
    }

    private async Task RunJobAsync(Guid jobId, Guid? repositoryId, bool forceFull, CancellationToken cancellationToken)
    {
        var jobSw = Stopwatch.StartNew();
        logger.LogInformation(
            "Indexer job {JobId} started (scope: {Scope})",
            jobId, repositoryId is { } r ? $"repository {r}" : "all repositories");
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            var resolver = scope.ServiceProvider.GetRequiredService<IDependencyResolver>();
            var streamIndexer = scope.ServiceProvider.GetRequiredService<IStreamIndexer>();
            var list = await repos.ListAsync(cancellationToken).ConfigureAwait(false);
            var targets = list.Where(r => r is { IsOnline: true, IsEnabled: true }
                                          && (repositoryId is null || r.Id == repositoryId))
                .ToList();

            int indexed = 0, skipped = 0, pruned = 0, errors = 0;
            for (var i = 0; i < targets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var repo = targets[i];
                Set(s => s with
                {
                    State = IndexerJobState.Ingesting,
                    PhaseMessage = $"Ingesting {repo.Name}",
                    Done = i,
                    Total = targets.Count,
                    WorkingSetBytes = Process.GetCurrentProcess().WorkingSet64,
                });

                var progress = new CallbackProgress(report =>
                {
                    Set(s => s with
                    {
                        State = IndexerJobState.Ingesting,
                        PhaseMessage = report.Message,
                        Done = report.Done,
                        Total = report.Total > 0 ? report.Total : s.Total,
                        WorkingSetBytes = Process.GetCurrentProcess().WorkingSet64,
                    });
                });

                var media = Enum.TryParse<MediaType>(repo.MediaType, ignoreCase: true, out var mt)
                    ? mt
                    : MediaType.Unknown;

                var outcome = await streamIndexer.IndexRepositoryAsync(
                    repo.Id, repo.MountPath, media, progress, forceFull, cancellationToken).ConfigureAwait(false);
                indexed += outcome.Indexed;
                skipped += outcome.Skipped;
                pruned += outcome.Pruned;
                errors += outcome.Corrupt;
            }

            Set(s => s with { State = IndexerJobState.Resolving, PhaseMessage = "Resolving dependencies…" });
            await writeQueue.EnqueueAsync(
                async ct => { await resolver.ResolveAllAsync(ct).ConfigureAwait(false); return true; },
                WritePriority.Bulk, cancellationToken).ConfigureAwait(false);

            Set(_ => new IndexerStatus(
                IndexerProtocol.Version, IndexerJobState.Completed, jobId,
                $"Indexed {indexed} (skipped {skipped}, pruned {pruned})",
                indexed, Math.Max(1, indexed), Process.GetCurrentProcess().WorkingSet64,
                0, errors, DateTime.UtcNow.Ticks, null));
            logger.LogInformation(
                "Indexer job {JobId} completed in {ElapsedMs} ms: {Indexed} indexed, {Skipped} skipped, " +
                "{Pruned} pruned, {Errors} errors across {Repos} repositories (working set {WorkingSetMb} MB)",
                jobId, jobSw.ElapsedMilliseconds, indexed, skipped, pruned, errors, targets.Count,
                Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024));
        }
        catch (OperationCanceledException)
        {
            Set(s => s with { State = IndexerJobState.Cancelled, PhaseMessage = "Cancelled" });
            logger.LogInformation("Indexer job {JobId} cancelled after {ElapsedMs} ms", jobId, jobSw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Indexer job {JobId} failed", jobId);
            Set(s => s with { State = IndexerJobState.Failed, Error = ex.Message, PhaseMessage = "Failed" });
        }
        finally
        {
            PendingStart? next;
            lock (_gate)
            {
                if (_activeJob == jobId)
                    _activeJob = null;
                // Clear before starting follow-up — we're still inside this Task until finally returns.
                _running = null;
                next = _pending;
                _pending = null;
            }

            if (next is { } pending)
            {
                logger.LogInformation(
                    "Indexer starting queued follow-up {JobId} ({Scope}, forceFull={ForceFull})",
                    pending.JobId,
                    pending.RepositoryId is { } followRepo ? $"repository {followRepo}" : "all repositories",
                    pending.ForceFull);
                lock (_gate)
                {
                    // Another Start may have raced in after we cleared _running; if so, re-queue.
                    if (_running is { IsCompleted: false })
                    {
                        _pending = _pending is null
                            ? pending
                            : _pending.Merge(pending.RepositoryId, pending.ForceFull);
                    }
                    else
                    {
                        BeginJob_NoLock(pending.RepositoryId, pending.ForceFull, pending.JobId);
                    }
                }
            }
        }
    }

    private void CancelActive(Guid? jobId)
    {
        lock (_gate)
        {
            if (jobId is { } id && _activeJob != id)
                return;
            _pending = null;
            _jobCts?.Cancel();
        }
    }

    private sealed record PendingStart(Guid? RepositoryId, bool ForceFull, Guid JobId)
    {
        public PendingStart Merge(Guid? repositoryId, bool forceFull)
        {
            // null repositoryId = IndexAll. Different repos → escalate to IndexAll.
            Guid? scope = RepositoryId is null || repositoryId is null
                ? null
                : RepositoryId == repositoryId ? RepositoryId : null;
            return this with { RepositoryId = scope, ForceFull = ForceFull || forceFull };
        }
    }

    private void Set(Func<IndexerStatus, IndexerStatus> mutate)
    {
        lock (_gate) _status = mutate(_status);
    }

    private static IndexerStatus Idle() =>
        new(IndexerProtocol.Version, IndexerJobState.Idle, null, null, 0, 0,
            Process.GetCurrentProcess().WorkingSet64, 0, 0, 0, null);

    private sealed class CallbackProgress(Action<ProgressReport> onReport) : IProgressSink
    {
        public void Report(ProgressReport report) => onReport(report);
    }
}
