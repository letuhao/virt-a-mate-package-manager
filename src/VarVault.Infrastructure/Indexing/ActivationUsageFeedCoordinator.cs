using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VarVault.Common.Diagnostics;
using VarVault.Domain.Analyzer;
using VarVault.Domain.Entities;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Debounces preset-activate usage feeds so rapid re-activations merge into one bulk write.
/// </summary>
public sealed class ActivationUsageFeedCoordinator(
    IWriteQueue writeQueue,
    ILogger<ActivationUsageFeedCoordinator>? logger = null)
{
    public static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private HashSet<long> _pending = [];
    private CancellationTokenSource? _debounceCts;

    public void Enqueue(IReadOnlyList<long> packageIds)
    {
        if (packageIds.Count == 0)
            return;

        CancellationToken debounceToken;
        lock (_gate)
        {
            foreach (var id in packageIds)
            {
                if (id > 0)
                    _pending.Add(id);
            }

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            debounceToken = _debounceCts.Token;
        }

        _ = DebounceFlushAsync(debounceToken);
    }

    private async Task DebounceFlushAsync(CancellationToken debounceToken)
    {
        try
        {
            await Task.Delay(DebounceWindow, debounceToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (debounceToken.IsCancellationRequested)
        {
            return;
        }

        List<long> batch;
        lock (_gate)
        {
            if (_pending.Count == 0)
                return;
            batch = _pending.ToList();
            _pending.Clear();
        }

        foreach (var chunk in batch.Chunk(EfUsageAnalyzer.UsageChunkSize))
        {
            var ids = chunk.ToArray();
            _ = writeQueue.EnqueueScopedAsync(async (sp, ct) =>
            {
                await sp.GetRequiredService<IUsageCatalogWriter>()
                    .RecordManyAndRecomputeAsync(ids, UsageKind.Activate, ct)
                    .ConfigureAwait(false);
            }, WritePriority.Bulk, CancellationToken.None).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Telemetry.UsageFeedFailed.Add(1);
                    logger?.LogWarning(t.Exception!.GetBaseException(),
                        "Usage feed failed after activation ({Count} packages); install links are intact.", ids.Length);
                }
            }, TaskScheduler.Default);
        }
    }
}
