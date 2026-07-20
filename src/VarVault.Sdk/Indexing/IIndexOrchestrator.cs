using VarVault.Common;

namespace VarVault.Sdk.Indexing;

/// <summary>Outcome of a full index run: enumeration + resolution + usage recompute rolled up.</summary>
public sealed record IndexRunSummary(
    int Repositories,
    int Indexed,
    int Skipped,
    int Pruned,
    int Resolved,
    int Missing,
    int UsageRecomputed);

/// <summary>
/// The runtime trigger that makes the catalog non-empty (BE-N0). Indexes the enabled/online repositories,
/// then runs dependency resolution and usage recompute so the read model is complete — the piece that was
/// missing at runtime (indexing only ran in tests). The shell wraps this in an <c>IJobQueue</c> job.
/// (16-checklist BE-N0.)
/// </summary>
public interface IIndexOrchestrator
{
    /// <summary>Index every enabled+online repository, then resolve dependencies and recompute usage.
    /// <paramref name="progress"/> reports the live per-phase status into the job handle (scan → index → resolve → usage).</summary>
    Task<IndexRunSummary> IndexAllAsync(IProgressSink? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Index one repository, then resolve dependencies and recompute usage.</summary>
    Task<IndexRunSummary> IndexRepositoryAsync(System.Guid repositoryId, IProgressSink? progress = null, CancellationToken cancellationToken = default);
}
