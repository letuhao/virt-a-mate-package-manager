using VarVault.Domain.Entities;

namespace VarVault.Domain.Analyzer;

/// <summary>
/// Records usage inside an existing write-queue scoped job (no re-enqueue).
/// Resolved from scoped DI within <c>IWriteQueue.EnqueueScopedAsync</c> callbacks.
/// </summary>
public interface IUsageCatalogWriter
{
    Task RecordManyAndRecomputeAsync(
        IReadOnlyList<long> packageIds,
        UsageKind kind,
        CancellationToken cancellationToken = default);
}
