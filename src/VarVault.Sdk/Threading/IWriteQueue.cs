using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    /// Enqueue a catalog write that runs inside a fresh DI scope so the worker thread never
    /// shares the caller's scoped <c>DbContext</c>. Prefer this for all EF mutations.
    /// </summary>
    Task<T> EnqueueScopedAsync<T>(Func<IServiceProvider, CancellationToken, Task<T>> write, WritePriority priority = WritePriority.Normal, CancellationToken cancellationToken = default);

    /// <summary>Scoped variant of <see cref="EnqueueAsync(Func{CancellationToken, Task}, WritePriority, CancellationToken)"/>.</summary>
    Task EnqueueScopedAsync(Func<IServiceProvider, CancellationToken, Task> write, WritePriority priority = WritePriority.Normal, CancellationToken cancellationToken = default);
}

/// <summary>Interactive writes jump ahead of bulk writes.</summary>
public enum WritePriority { Interactive = 0, Normal = 1, Bulk = 2 }
