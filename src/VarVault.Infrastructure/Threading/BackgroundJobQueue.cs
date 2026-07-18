using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using VarVault.Common;
using VarVault.Common.Diagnostics;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Threading;

/// <summary>
/// Channel-backed background job queue with bounded concurrency. Long-running work
/// (indexing, migration, fixing) runs off the UI thread; each job is tracked and
/// cancellable via its <see cref="JobHandle"/>.
/// </summary>
internal sealed class BackgroundJobQueue : IJobQueue, IAsyncDisposable
{
    private readonly Channel<(JobHandle handle, Func<JobContext, Task> work)> _channel =
        Channel.CreateUnbounded<(JobHandle, Func<JobContext, Task>)>(new UnboundedChannelOptions { SingleReader = false });
    private readonly ConcurrentDictionary<JobHandle, byte> _active = new();
    private readonly Task[] _workers;
    private readonly ObservableGauge<int> _activeGauge;

    public BackgroundJobQueue(int degreeOfParallelism = 0)
    {
        _activeGauge = Telemetry.Meter.CreateObservableGauge("varvault.jobs.active", () => _active.Count);
        var degree = degreeOfParallelism > 0 ? degreeOfParallelism : Math.Max(1, Environment.ProcessorCount - 1);
        _workers = new Task[degree];
        for (var i = 0; i < degree; i++)
            _workers[i] = Task.Run(WorkerAsync);
    }

    public IReadOnlyList<JobHandle> Active => _active.Keys.ToArray();

    public JobHandle Enqueue(string name, Func<JobContext, Task> work)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(work);
        var handle = new JobHandle(name);
        _active[handle] = 0;
        _channel.Writer.TryWrite((handle, work));
        return handle;
    }

    private async Task WorkerAsync()
    {
        await foreach (var (handle, work) in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            handle.State = JobState.Running;
            using var activity = Telemetry.StartActivity($"job:{handle.Name}");
            Telemetry.JobsStarted.Add(1);
            try
            {
                await work(new JobContext(handle.Cancellation, handle)).ConfigureAwait(false);
                handle.State = JobState.Completed;
                Telemetry.JobsCompleted.Add(1);
            }
            catch (OperationCanceledException)
            {
                handle.State = JobState.Cancelled;
                Telemetry.JobsCompleted.Add(1);
            }
            catch (Exception ex)
            {
                handle.State = JobState.Failed;
                handle.Error = new Error("job.failed", ex.Message);
                Telemetry.JobsFailed.Add(1);
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            }
            finally
            {
                _active.TryRemove(handle, out _);
                handle.DisposeSource();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        try { await Task.WhenAll(_workers).ConfigureAwait(false); } catch (OperationCanceledException) { }
    }
}
