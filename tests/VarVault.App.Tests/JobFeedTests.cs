using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.ViewModels;
using VarVault.Sdk.Threading;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// GA-2 · The jobs panel populates from the live <see cref="IJobQueue"/> — active jobs render, an empty
/// queue clears the unread dot, and a row can cancel its job. (18-gap GA-2.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public class JobFeedTests
{
    private static ShellViewModel Shell(IJobQueue queue) =>
        new(new Dictionary<string, object>(), initial: "library", jobQueue: queue);

    /// <summary>Bounded poll (no sleep-then-assert): waits up to ~2s for a condition.</summary>
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition(), "condition not met within timeout");
    }

    [Fact]
    public async Task Active_jobs_from_the_queue_render_as_rows()
    {
        await using var host = TestHost.Create(withPersistence: true);
        var queue = host.Host.Services.GetRequiredService<IJobQueue>();
        var shell = Shell(queue);

        // A gated job stays Running until we release it, so it is observable as Active.
        var gate = new TaskCompletionSource();
        var handle = queue.Enqueue("Indexing — test", async _ => await gate.Task);

        await Until(() => queue.Active.Count > 0);
        shell.RefreshJobsFromQueue();

        Assert.True(shell.HasActiveJobs);
        Assert.Contains(shell.ActiveJobs, j => j.Name.Contains("Indexing"));

        gate.SetResult();
        await Until(() => queue.Active.Count == 0);
        shell.RefreshJobsFromQueue();
        Assert.False(shell.HasActiveJobs);
        Assert.Empty(shell.ActiveJobs);
    }

    [Fact]
    public async Task Cancel_on_a_row_cancels_the_job()
    {
        await using var host = TestHost.Create(withPersistence: true);
        var queue = host.Host.Services.GetRequiredService<IJobQueue>();
        var shell = Shell(queue);

        var gate = new TaskCompletionSource();
        var handle = queue.Enqueue("Migrating — test", async _ => await gate.Task);
        await Until(() => queue.Active.Count > 0);
        shell.RefreshJobsFromQueue();

        var row = shell.ActiveJobs.First();
        row.Cancel();
        Assert.True(row.Cancellation.IsCancellationRequested);

        gate.SetResult();
    }
}
