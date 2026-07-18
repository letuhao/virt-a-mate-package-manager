namespace VarVault.Sdk.Indexing;

/// <summary>Summary counts from indexing a repository (SDK-safe primitives only).</summary>
public sealed record IndexResult(int Indexed, int Skipped, int Pruned, int Corrupt, int Unrecognized);

/// <summary>
/// Triggers indexing of a registered repository. The public boundary the UI/CLI call; the
/// implementation lives in the Indexing module and orchestrates enumeration → inspection → catalog
/// writes through Domain/SDK seams. (Architecture doc 11; IDX-1.)
/// </summary>
public interface IIndexingService
{
    /// <summary>Index (or incrementally re-index) the repository at <paramref name="repositoryMountPath"/>.</summary>
    Task<IndexResult> IndexRepositoryAsync(
        Guid repositoryId,
        string repositoryMountPath,
        CancellationToken cancellationToken = default);
}
