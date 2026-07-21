using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using VarVault.Common.Diagnostics;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Threading;

/// <summary>
/// Single-consumer write queue. All catalog mutations run one-at-a-time on a dedicated
/// worker, so SQLite's single-writer model holds; interactive writes are served before
/// bulk writes. Emits depth/throughput/duration metrics for monitoring. (Data-arch §5.4.)
/// </summary>
internal sealed class WriteQueue : IWriteQueue, IAsyncDisposable
{
    private readonly ConcurrentQueue<Func<CancellationToken, Task>>[] _queues =
    [
        new(), // Interactive
        new(), // Normal
        new(), // Bulk
    ];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _consumer;
    private readonly ObservableGauge<int> _depthGauge;
    private readonly IGlobalWriteLock _globalLock;

    public WriteQueue(IGlobalWriteLock? globalLock = null)
    {
        _globalLock = globalLock ?? new NullWriteLock();
        _depthGauge = Telemetry.Meter.CreateObservableGauge("varvault.writequeue.depth", () => Depth);
        _consumer = Task.Run(ConsumeAsync);
    }

    /// <summary>Pending write count across all priorities (for monitoring/health).</summary>
    public int Depth => _queues[0].Count + _queues[1].Count + _queues[2].Count;

    public Task EnqueueAsync(Func<CancellationToken, Task> write, WritePriority priority = WritePriority.Normal, CancellationToken cancellationToken = default)
        => EnqueueAsync(async ct => { await write(ct).ConfigureAwait(false); return true; }, priority, cancellationToken);

    public Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> write, WritePriority priority = WritePriority.Normal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        _queues[(int)priority].Enqueue(async workerToken =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(workerToken, cancellationToken);
            using var activity = Telemetry.StartActivity("write");
            var start = Stopwatch.GetTimestamp();
            try
            {
                var result = await write(linked.Token).ConfigureAwait(false);
                Telemetry.WritesCompleted.Add(1);
                Telemetry.WriteDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                tcs.SetResult(result);
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetCanceled(linked.Token);
            }
            catch (Exception ex)
            {
                Telemetry.WritesFailed.Add(1);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                tcs.TrySetException(ex);
            }
        });
        _signal.Release();
        return tcs.Task;
    }

    private async Task ConsumeAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try { await _signal.WaitAsync(_shutdown.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            if (!TryDequeue(out var work))
                continue;

            // Serialize across processes: the GUI and the out-of-process indexer can both hold this
            // WriteQueue, but only one may write the catalog at a time. (A12 single-writer.)
            IAsyncDisposable? scope = null;
            var waitStarted = Stopwatch.GetTimestamp();
            try { scope = await _globalLock.AcquireAsync(_shutdown.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            Telemetry.WriteLockWaitDurationMs.Record(Stopwatch.GetElapsedTime(waitStarted).TotalMilliseconds);

            var holdStarted = Stopwatch.GetTimestamp();
            try
            {
                await work(_shutdown.Token).ConfigureAwait(false);
            }
            finally
            {
                if (scope is not null)
                    await scope.DisposeAsync().ConfigureAwait(false);
                Telemetry.WriteLockHoldDurationMs.Record(Stopwatch.GetElapsedTime(holdStarted).TotalMilliseconds);
            }
        }
    }

    private bool TryDequeue(out Func<CancellationToken, Task> work)
    {
        foreach (var queue in _queues) // Interactive → Normal → Bulk
            if (queue.TryDequeue(out work!))
                return true;
        work = null!;
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        _signal.Release();
        try { await _consumer.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _shutdown.Dispose();
        _signal.Dispose();
    }
}
