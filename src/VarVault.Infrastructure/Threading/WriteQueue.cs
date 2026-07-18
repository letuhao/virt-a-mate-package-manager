using System.Collections.Concurrent;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Threading;

/// <summary>
/// Single-consumer write queue. All catalog mutations run one-at-a-time on a dedicated
/// worker, so SQLite's single-writer model holds; interactive writes are served before
/// bulk writes. (Data-architecture §5.4.)
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

    public WriteQueue() => _consumer = Task.Run(ConsumeAsync);

    public Task EnqueueAsync(Func<CancellationToken, Task> write, WritePriority priority = WritePriority.Normal, CancellationToken cancellationToken = default)
        => EnqueueAsync(async ct => { await write(ct).ConfigureAwait(false); return true; }, priority, cancellationToken);

    public Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> write, WritePriority priority = WritePriority.Normal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        _queues[(int)priority].Enqueue(async workerToken => {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(workerToken, cancellationToken);
            try { tcs.SetResult(await write(linked.Token).ConfigureAwait(false)); }
            catch (OperationCanceledException) { tcs.TrySetCanceled(linked.Token); }
            catch (Exception ex) { tcs.TrySetException(ex); }
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

            if (TryDequeue(out var work))
                await work(_shutdown.Token).ConfigureAwait(false);
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
