using VarVault.Common;

namespace VarVault.Sdk.Threading;

/// <summary>
/// Background work queue for long-running, cancellable operations (indexing, migration,
/// fixing). Channel-backed; jobs run off the UI thread with bounded concurrency. Enqueue
/// returns immediately with a handle the UI can track/cancel.
/// </summary>
public interface IJobQueue
{
    JobHandle Enqueue(string name, Func<JobContext, Task> work);
    IReadOnlyList<JobHandle> Active { get; }
}

/// <summary>Per-job context: cancellation + progress reporting.</summary>
public sealed record JobContext(CancellationToken Cancellation, IProgressSink Progress);

public enum JobState { Queued, Running, Completed, Failed, Cancelled }

/// <summary>A tracked background job. Cancel via <see cref="Cancel"/>; observe via <see cref="State"/>.</summary>
public sealed class JobHandle(string name) : IProgressSink
{
    private readonly CancellationTokenSource _cts = new();

    public string Name { get; } = name;
    public JobState State { get; internal set; } = JobState.Queued;
    public ProgressReport Progress { get; private set; }
    public Error? Error { get; internal set; }
    public CancellationToken Cancellation => _cts.Token;

    public void Cancel() => _cts.Cancel();
    public void Report(ProgressReport report) => Progress = report;
    internal void DisposeSource() => _cts.Dispose();
}
