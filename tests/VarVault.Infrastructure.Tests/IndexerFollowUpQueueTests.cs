using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Indexing;
using VarVault.Sdk.Indexer;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Import used to coalesce into a pre-copy IndexAll and treat that as "indexed" — Library ghosts.
/// Follow-up queue + wait-for-job-id must run a dedicated post-copy scan.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public sealed class IndexerFollowUpQueueTests
{
    [Fact]
    public async Task Start_while_busy_returns_follow_up_job_id_and_runs_after_active()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<(Guid RepoId, bool ForceFull)>();
        var fake = new GatedStreamIndexer(gate.Task, calls);

        await using var host = TestHost.Create(withPersistence: true, configure: services =>
        {
            services.RemoveAll<IStreamIndexer>();
            services.AddSingleton<IStreamIndexer>(fake);
        });

        var repos = host.Get<IRepositoryService>();
        var mount = Path.Combine(host.DataDirectory.Path, "repo");
        Directory.CreateDirectory(mount);
        var registered = await repos.RegisterAsync(new RegisterRepositoryRequest("R", mount));
        Assert.True(registered.IsSuccess);

        var worker = host.Get<IIndexerWorker>();
        var client = host.Get<InProcessIndexerClient>();

        var first = await client.StartIndexAllAsync(forceFull: false);
        Assert.True(first.IsSuccess);
        Assert.True(worker.IsBusy);

        // Mimic Import.Apply mid IndexAll — must not attach to the first job id.
        var follow = await client.StartIndexRepositoryAsync(registered.Value.Id, forceFull: true);
        Assert.True(follow.IsSuccess);
        Assert.NotEqual(first.Value, follow.Value);

        var statusWhileQueued = await client.GetStatusAsync();
        Assert.True(statusWhileQueued.IsSuccess);
        Assert.Equal(1, statusWhileQueued.Value.QueueDepth);
        Assert.Equal(first.Value, statusWhileQueued.Value.JobId); // active still first

        gate.SetResult(); // let first ingest finish

        await IndexerAwait.WaitForJobAsync(client, follow.Value, progress: null, CancellationToken.None);

        Assert.Equal(2, calls.Count);
        Assert.False(calls[0].ForceFull);
        Assert.Equal(registered.Value.Id, calls[1].RepoId);
        Assert.True(calls[1].ForceFull);

        var done = await client.GetStatusAsync();
        Assert.Equal(IndexerJobState.Completed, done.Value.State);
        Assert.Equal(follow.Value, done.Value.JobId);
        Assert.Equal(0, done.Value.QueueDepth);
    }

    [Fact]
    public async Task WaitForRepositoryIndex_does_not_return_on_unrelated_Completed()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<(Guid RepoId, bool ForceFull)>();
        var fake = new GatedStreamIndexer(gate.Task, calls);

        await using var host = TestHost.Create(withPersistence: true, configure: services =>
        {
            services.RemoveAll<IStreamIndexer>();
            services.AddSingleton<IStreamIndexer>(fake);
        });

        var repos = host.Get<IRepositoryService>();
        var mount = Path.Combine(host.DataDirectory.Path, "repo");
        Directory.CreateDirectory(mount);
        var registered = await repos.RegisterAsync(new RegisterRepositoryRequest("R", mount));
        Assert.True(registered.IsSuccess);

        var client = host.Get<InProcessIndexerClient>();
        _ = await client.StartIndexAllAsync();

        var waitTask = IndexerAwait.WaitForRepositoryIndexAsync(
            client, registered.Value.Id, forceFull: true, progress: null, CancellationToken.None);

        // Give the follow-up a moment to queue, then finish the first job.
        await Task.Delay(100);
        Assert.False(waitTask.IsCompleted);
        gate.SetResult();

        await waitTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(2, calls.Count);
        Assert.True(calls[^1].ForceFull);
    }

    private sealed class GatedStreamIndexer(Task gate, List<(Guid RepoId, bool ForceFull)> calls) : IStreamIndexer
    {
        public async Task<IndexOutcome> IndexRepositoryAsync(
            Guid repositoryId,
            string mountPath,
            MediaType mediaType,
            IProgressSink? progress = null,
            bool forceFull = false,
            CancellationToken cancellationToken = default)
        {
            lock (calls) calls.Add((repositoryId, forceFull));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new IndexOutcome(1, 0, 0, 0, 0);
        }
    }
}
