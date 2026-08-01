using VarVault.Common;
using VarVault.Sdk.Indexer;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Shared wait helpers for Import / Onboarding: never treat an unrelated Completed/Idle
/// (or a coalesced pre-copy IndexAll) as "our index finished".
/// </summary>
public static class IndexerAwait
{
    /// <summary>
    /// Start a repository index (<paramref name="forceFull"/>) and wait until <b>that</b> job
    /// reaches a terminal state. If the worker is busy, Start queues a follow-up and returns its
    /// job id — we wait for that id, not the already-running scan.
    /// </summary>
    public static async Task WaitForRepositoryIndexAsync(
        IIndexerClient indexer,
        Guid repositoryId,
        bool forceFull,
        IProgressSink? progress,
        CancellationToken cancellationToken)
    {
        var start = await indexer.StartIndexRepositoryAsync(repositoryId, forceFull, cancellationToken)
            .ConfigureAwait(false);
        if (start.IsFailure)
            throw new InvalidOperationException(start.Error.Message);

        await WaitForJobAsync(indexer, start.Value, progress, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WaitForJobAsync(
        IIndexerClient indexer,
        Guid jobId,
        IProgressSink? progress,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var status = await indexer.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.IsFailure)
                throw new InvalidOperationException(status.Error.Message);

            var s = status.Value;
            progress?.Report(new ProgressReport(
                s.Done, Math.Max(1, s.Total),
                s.PhaseMessage ?? $"Indexing… ({s.State})"));

            if (s.JobId == jobId)
            {
                if (s.State is IndexerJobState.Completed or IndexerJobState.Failed
                    or IndexerJobState.Cancelled)
                {
                    if (s.State == IndexerJobState.Failed)
                        throw new InvalidOperationException(s.Error ?? "index failed");
                    if (s.State == IndexerJobState.Cancelled)
                        throw new OperationCanceledException("index cancelled", cancellationToken);
                    return;
                }
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
