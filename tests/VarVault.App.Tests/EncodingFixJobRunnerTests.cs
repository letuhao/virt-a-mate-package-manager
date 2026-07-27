using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VarVault.App.Services;
using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.Sdk.Threading;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Encoding-fix jobs: enqueue is immediate, progress arrives, cancel propagates.</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class EncodingFixJobRunnerTests
{
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition(), "condition not met within timeout");
    }

    private static void Register(IServiceCollection services)
    {
        services.RemoveAll<IHealthService>();
        services.AddSingleton<IHealthService, CountingHealth>();
        services.AddSingleton<EncodingFixJobRunner>();
    }

    [Fact]
    public async Task Group_job_enqueues_reports_progress_and_completes()
    {
        await using var host = TestHost.Create(withPersistence: true, configure: Register);
        var runner = host.Host.Services.GetRequiredService<EncodingFixJobRunner>();
        var queue = host.Host.Services.GetRequiredService<IJobQueue>();
        var health = (CountingHealth)host.Host.Services.GetRequiredService<IHealthService>();
        var completed = 0;
        runner.AfterCompleted = () => Interlocked.Increment(ref completed);

        var job = runner.StartGroup("GBK");
        Assert.Contains(job.Handle, queue.Active);
        Assert.Contains("GBK", job.Handle.Name, StringComparison.Ordinal);

        var result = await job.Result;
        Assert.Equal(3, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.True(health.LastProgressTotal >= 3);
        await Until(() => completed == 1);
        await Until(() => queue.Active.Count == 0);
    }

    [Fact]
    public async Task Cancel_propagates_to_result()
    {
        await using var host = TestHost.Create(withPersistence: true, configure: s =>
        {
            s.RemoveAll<IHealthService>();
            s.AddSingleton<IHealthService>(_ => new CountingHealth(total: 50, delayMs: 40));
            s.AddSingleton<EncodingFixJobRunner>();
        });
        var runner = host.Host.Services.GetRequiredService<EncodingFixJobRunner>();
        var job = runner.StartVarFiles(Enumerable.Range(1, 50).Select(i => (long)i).ToList());
        await Until(() => job.Handle.State == JobState.Running);
        job.Handle.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.Result);
        Assert.Equal(JobState.Cancelled, job.Handle.State);
    }

    private sealed class CountingHealth(int total = 3, int delayMs = 0) : IHealthService
    {
        public int LastProgressTotal { get; private set; }

        public Task<IReadOnlyList<EncodingGroup>> EncodingGroupsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EncodingGroup>>([]);
        public Task<IReadOnlyList<IntegrityIssue>> IntegrityAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IntegrityIssue>>([]);
        public Task<IReadOnlyList<IntegrityIssue>> MissingMetaAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IntegrityIssue>>([]);
        public Task<IReadOnlyList<VamLoadIssue>> ScanVamLoadAsync(
            VamLoadScanScope scope, IProgressSink? progress = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VamLoadIssue>>([]);
        public Task<Result<long>> FixAsync(long varFileId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(varFileId));
        public Task<Result<long>> FixDuplicateEntriesAsync(
            long varFileId,
            IReadOnlyDictionary<string, string>? keepByNormalizedKey = null,
            CancellationToken ct = default) =>
            Task.FromResult(Result.Success(varFileId));
        public Task<Result<IReadOnlyList<DuplicateEntryCollision>>> GetDuplicateEntryCollisionsAsync(
            long varFileId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success<IReadOnlyList<DuplicateEntryCollision>>([]));
        public Task<BulkActionResult> FixGroupAsync(string? codepageFilter, CancellationToken ct = default) =>
            FixGroupAsync(codepageFilter, IProgressSink.Null, ct);
        public async Task<BulkActionResult> FixGroupAsync(string? codepageFilter, IProgressSink progress, CancellationToken ct = default)
        {
            var ids = Enumerable.Range(1, total).Select(i => (long)i).ToList();
            return await FixManyAsync(ids, progress, ct).ConfigureAwait(false);
        }
        public async Task<BulkActionResult> FixManyAsync(IReadOnlyList<long> varFileIds, IProgressSink? progress = null, CancellationToken ct = default)
        {
            progress ??= IProgressSink.Null;
            for (var i = 0; i < varFileIds.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(new ProgressReport(i + 1, varFileIds.Count, $"Fixing {i + 1}"));
                LastProgressTotal = varFileIds.Count;
                if (delayMs > 0)
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
            return new BulkActionResult(varFileIds.Count, 0);
        }
        public Task<BulkActionResult> FixDuplicateEntriesManyAsync(IReadOnlyList<long> varFileIds, IProgressSink? progress = null, CancellationToken ct = default) =>
            Task.FromResult(new BulkActionResult(varFileIds.Count, 0));
    }
}
