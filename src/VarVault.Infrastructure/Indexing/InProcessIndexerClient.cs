using VarVault.Common;
using VarVault.Sdk.Indexer;

namespace VarVault.Infrastructure.Indexing;

/// <summary>In-process client that talks to <see cref="IIndexerWorker"/> directly (tests + same-host fallback).</summary>
public sealed class InProcessIndexerClient(IIndexerWorker worker) : IIndexerClient
{
    public async Task<Result<IndexerStatus>> PingAsync(CancellationToken cancellationToken = default)
    {
        var status = await worker.HandleAsync(new IndexerCommand(IndexerCommandKind.Ping), cancellationToken).ConfigureAwait(false);
        return Result.Success(status);
    }

    public async Task<Result<Guid>> StartIndexAllAsync(bool forceFull = false, CancellationToken cancellationToken = default)
    {
        var status = await worker.HandleAsync(
            new IndexerCommand(IndexerCommandKind.StartIndexAll, ForceFull: forceFull), cancellationToken).ConfigureAwait(false);
        return status.JobId is { } id
            ? Result.Success(id)
            : Result.Failure<Guid>("indexer.start", status.Error ?? "failed to start");
    }

    public async Task<Result<Guid>> StartIndexRepositoryAsync(Guid repositoryId, bool forceFull = false, CancellationToken cancellationToken = default)
    {
        var status = await worker.HandleAsync(
            new IndexerCommand(IndexerCommandKind.StartIndexRepository, RepositoryId: repositoryId, ForceFull: forceFull),
            cancellationToken).ConfigureAwait(false);
        return status.JobId is { } id
            ? Result.Success(id)
            : Result.Failure<Guid>("indexer.start", status.Error ?? "failed to start");
    }

    public async Task<Result> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await worker.HandleAsync(new IndexerCommand(IndexerCommandKind.CancelJob, JobId: jobId), cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result<IndexerStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await worker.HandleAsync(new IndexerCommand(IndexerCommandKind.GetStatus), cancellationToken).ConfigureAwait(false);
        return Result.Success(status);
    }

    // In-process: worker lifetime is tied to this process, so ownership is a no-op.
    public Task<Result> RegisterOwnerAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    public Task<Result> UnregisterOwnerAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
}
