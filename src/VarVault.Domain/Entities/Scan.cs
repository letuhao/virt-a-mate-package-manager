namespace VarVault.Domain.Entities;

/// <summary>
/// One durable indexing run (discovery generation + phase). Crash-resumable; workers reclaim
/// expired leases rather than rebuilding in-memory plans. (A15.)
/// </summary>
public sealed class ScanRun
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public Guid? RepositoryId { get; set; }
    public long Generation { get; set; }
    public ScanPhase Phase { get; set; } = ScanPhase.Discovering;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long Discovered { get; set; }
    public long Ingested { get; set; }
    public long Skipped { get; set; }
    public long Failed { get; set; }
    public long Pruned { get; set; }
    public string? Error { get; set; }

    // Repository fingerprint captured at discovery for the auto-index fast-path (A16). Null on runs
    // predating the feature or not yet past discovery; only a Completed run's signature is trusted.
    public long? SigFileCount { get; set; }
    public long? SigTotalBytes { get; set; }
    public long? SigNewestMtimeTicks { get; set; }
    public long? SigPathsHash { get; set; }
}

/// <summary>
/// Durable dirty marker for derived state (read model / FTS). Survives process death so a
/// crash between upsert and refresh cannot leave permanently stale rows. (A15 / M7.)
/// </summary>
public sealed class DirtyPackage
{
    public long PackageId { get; set; }
    public DateTime MarkedAt { get; set; }
    public string Reason { get; set; } = "ingest";
}
