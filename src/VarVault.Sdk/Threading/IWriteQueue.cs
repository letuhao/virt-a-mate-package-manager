namespace VarVault.Sdk.Threading;

/// <summary>
/// Serializes all catalog writes through a single consumer, so SQLite's single-writer
/// model is honored and interactive writes (favorite, activate) can be prioritized over
/// bulk writes. Reads never go through here. This is the one sanctioned path for mutating
/// the catalog. (Data-architecture §5.4.)
/// </summary>
public interface IWriteQueue
{
    /// <summary>Enqueue a write and await its completion (and result).</summary>
    Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> write, WritePriority priority = WritePriority.Normal, CancellationToken cancellationToken = default);

    /// <summary>Enqueue a write and await its completion.</summary>
    Task EnqueueAsync(Func<CancellationToken, Task> write, WritePriority priority = WritePriority.Normal, CancellationToken cancellationToken = default);
}

/// <summary>Interactive writes jump ahead of bulk writes.</summary>
public enum WritePriority { Interactive = 0, Normal = 1, Bulk = 2 }
