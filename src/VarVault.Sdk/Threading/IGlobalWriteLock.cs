namespace VarVault.Sdk.Threading;

/// <summary>
/// Cross-process catalog write mutex. Every <see cref="IWriteQueue"/> action acquires it before running,
/// so at most one process writes the catalog at any instant — the true single-writer invariant when the
/// GUI and the out-of-process indexer are both alive. Reads never take it. (A12 single-writer.)
/// </summary>
public interface IGlobalWriteLock
{
    /// <summary>Acquire exclusive write access; dispose the returned scope to release.</summary>
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default);
}
