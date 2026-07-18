using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Common;
using VarVault.Host;
using VarVault.Sdk.Threading;
using VarVault.TestKit;

namespace VarVault.Host.Tests;

[Trait("Category", TestCategories.Integration)]
public class ThreadingTests
{
    [Fact]
    public async Task WriteQueue_runs_writes_one_at_a_time()
    {
        await using var host = Bootstrap.BuildDefault();
        var queue = host.Services.GetRequiredService<IWriteQueue>();

        var concurrent = 0;
        var maxConcurrent = 0;
        var completed = new ConcurrentQueue<int>();

        var tasks = Enumerable.Range(0, 12).Select(i =>
            queue.EnqueueAsync(async ct =>
            {
                var c = Interlocked.Increment(ref concurrent);
                maxConcurrent = Math.Max(maxConcurrent, c);
                await Task.Delay(3, ct);
                completed.Enqueue(i);
                Interlocked.Decrement(ref concurrent);
            })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, maxConcurrent); // single-writer honored
        Assert.Equal(12, completed.Count);
    }

    [Fact]
    public async Task WriteQueue_returns_result()
    {
        await using var host = Bootstrap.BuildDefault();
        var queue = host.Services.GetRequiredService<IWriteQueue>();

        var result = await queue.EnqueueAsync(_ => Task.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task JobQueue_runs_job_and_reports_progress()
    {
        await using var host = Bootstrap.BuildDefault();
        var jobs = host.Services.GetRequiredService<IJobQueue>();

        var ran = new TaskCompletionSource();
        var handle = jobs.Enqueue("test", async ctx =>
        {
            ctx.Progress.Report(new ProgressReport(1, 1, "done"));
            await Task.Delay(3, ctx.Cancellation);
            ran.SetResult();
        });

        await ran.Task;
        for (var i = 0; i < 100 && handle.State == JobState.Running; i++)
            await Task.Delay(10);

        Assert.Equal(JobState.Completed, handle.State);
        Assert.Equal(1, handle.Progress.Done);
    }
}
