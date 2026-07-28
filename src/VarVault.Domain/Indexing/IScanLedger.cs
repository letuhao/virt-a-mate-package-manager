using VarVault.Common;
using VarVault.Domain.Entities;

namespace VarVault.Domain.Indexing;

/// <summary>Durable scan-run + lease claims. Implemented by Infrastructure. (A15.)</summary>
public interface IScanLedger
{
    Task<ScanRun> BeginRunAsync(Guid? repositoryId, CancellationToken cancellationToken = default);
    Task SetPhaseAsync(long runId, ScanPhase phase, CancellationToken cancellationToken = default);
    Task CompleteAsync(long runId, ScanPhase terminal, string? error = null, CancellationToken cancellationToken = default);
    Task UpsertDiscoveryAsync(long runId, Guid repositoryId, ScannedVar scanned, CancellationToken cancellationToken = default);
    Task UpsertDiscoveryBatchAsync(long runId, Guid repositoryId, IReadOnlyList<ScannedVar> scanned, CancellationToken cancellationToken = default);
    Task<int> MarkVanishedAsync(long runId, Guid repositoryId, CancellationToken cancellationToken = default);
    Task ReclaimExpiredLeasesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<long>> ClaimWorkAsync(long runId, Guid repositoryId, Guid leaseOwner, int take, CancellationToken cancellationToken = default);
    Task MarkRawStoredAsync(long varFileId, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(long varFileId, string error, CancellationToken cancellationToken = default);
    Task CheckpointWalAsync(CancellationToken cancellationToken = default);
    Task AnalyzeAsync(CancellationToken cancellationToken = default);
    Task<ScanRun?> GetRunAsync(long runId, CancellationToken cancellationToken = default);

    /// <summary>Persist the repository fingerprint captured during discovery on this run. (A16.)</summary>
    Task SetSignatureAsync(long runId, RepositorySignature signature, CancellationToken cancellationToken = default);

    /// <summary>The fingerprint of the most recent <b>completed</b> scan of a repository, or null. (A16.)</summary>
    Task<RepositorySignature?> GetLastCompletedSignatureAsync(Guid repositoryId, CancellationToken cancellationToken = default);

    /// <summary>True if any var in the repository still needs ingest or Library read-model materialization. (A16.)</summary>
    Task<bool> HasPendingWorkAsync(Guid repositoryId, CancellationToken cancellationToken = default);
}

/// <summary>Durable dirty-package set. (A15.)</summary>
public interface IDurableDirtySet
{
    Task MarkAsync(long packageId, string reason = "ingest", CancellationToken cancellationToken = default);
    Task MarkManyAsync(IEnumerable<long> packageIds, string reason = "ingest", CancellationToken cancellationToken = default);

    /// <summary>
    /// Peek the oldest dirty package ids without removing them. Call
    /// <see cref="AcknowledgeAsync"/> only after <c>RefreshReadModelAsync</c> commits.
    /// </summary>
    Task<IReadOnlyList<long>> PeekBatchAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>Drop dirty marks after a successful read-model refresh (crash-safe with Peek).</summary>
    Task AcknowledgeAsync(IReadOnlyList<long> packageIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Peek + acknowledge in one step. Prefer Peek/Acknowledge around refresh so a crash
    /// between drain and refresh cannot lose the dirty mark permanently.
    /// </summary>
    Task<IReadOnlyList<long>> DrainBatchAsync(int take, CancellationToken cancellationToken = default);
}

/// <summary>Bounded raw-first repository indexer used by the worker process. (A12–A14.)</summary>
public interface IStreamIndexer
{
    Task<IndexOutcome> IndexRepositoryAsync(
        Guid repositoryId,
        string mountPath,
        MediaType mediaType,
        IProgressSink? progress = null,
        bool forceFull = false,
        CancellationToken cancellationToken = default);
}
