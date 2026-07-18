namespace VarVault.Domain.Indexing;

/// <summary>
/// Discovers <c>.var</c> files under a repository root without opening any file. Implemented by
/// Infrastructure (filesystem); consumed by the indexing orchestrator. (IDX-1.)
/// </summary>
public interface IRepositoryEnumerator
{
    /// <summary>
    /// Enumerate vars under <paramref name="repositoryRoot"/>, skipping reparse points (symlinks/
    /// junctions) and the app's own link-farm directories. Quarantined vars are tagged (not treated
    /// as live); pass <paramref name="includeQuarantined"/> = false to omit them entirely.
    /// </summary>
    IEnumerable<ScannedVar> Enumerate(
        string repositoryRoot,
        bool includeQuarantined = true,
        CancellationToken cancellationToken = default);
}
