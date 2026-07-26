using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;
using VarVault.Sdk.Settings;
using VarVault.Sdk.Threading;

namespace VarVault.App.Services;

/// <summary>
/// Idle auto-rebalance: when <c>automation.auto_rebalance</c> is on and the job queue is quiet,
/// build a tiering plan and migrate a capped batch (ledger-gated in MigrationRunner).
/// </summary>
public sealed class IdleAutoRebalanceService(
    IServiceScopeFactory scopes,
    IJobQueue jobQueue,
    ILogger<IdleAutoRebalanceService>? logger = null) : IAsyncDisposable
{
    public const int DefaultBatchSize = 8;
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _running;

    public void Start(TimeSpan? interval = null)
    {
        if (_cts is not null)
            return;
        _cts = new CancellationTokenSource();
        var period = interval ?? DefaultInterval;
        _loop = Task.Run(() => LoopAsync(period, _cts.Token));
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null)
            return;
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }
        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    private async Task LoopAsync(TimeSpan period, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(period);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                // Fire-and-forget onto the job queue when work exists; ignore null (disabled/idle).
                _ = await TryEnqueueAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Idle auto-rebalance tick failed");
            }
        }
    }

    /// <summary>
    /// One idle tick: if enabled and no heavy jobs, enqueue a capped migrate batch.
    /// Returns the job handle when work was queued; null when skipped.
    /// </summary>
    public async Task<JobHandle?> TryEnqueueAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return null;
        try
        {
            if (jobQueue.Active.Any(j => j.State is JobState.Queued or JobState.Running))
                return null;

            using var scope = scopes.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            if (!await settings.GetBoolAsync(SettingKeys.AutoRebalance, false, cancellationToken).ConfigureAwait(false))
                return null;

            var requests = await BuildBatchAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
            if (requests.Count == 0)
                return null;

            return jobQueue.Enqueue("Auto-rebalance tiers", async ctx =>
            {
                using var jobScope = scopes.CreateScope();
                var migration = jobScope.ServiceProvider.GetRequiredService<IMigrationService>();
                var result = await migration.RunAsync(requests, ctx.Cancellation).ConfigureAwait(false);
                ctx.Progress.Report(new Common.ProgressReport(
                    result.Moved, Math.Max(1, result.Moved + result.Failed),
                    $"Moved {result.Moved}, failed {result.Failed}"));
            });
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary>Build migration requests without enqueueing — for tests / dry checks.</summary>
    public static async Task<IReadOnlyList<MigrationRequest>> BuildBatchAsync(
        IServiceProvider sp,
        CancellationToken cancellationToken = default,
        int batchSize = DefaultBatchSize)
    {
        var tiering = sp.GetRequiredService<ITieringService>();
        var repositories = sp.GetRequiredService<IRepositoryService>();

        var plan = await tiering.BuildPlanAsync(cancellationToken).ConfigureAwait(false);
        if (plan.Proposals.Count == 0)
            return [];

        var repos = await repositories.ListAsync(cancellationToken).ConfigureAwait(false);
        var targetByTier = repos
            .Where(r => r.IsOnline && r.IsEnabled
                        && !string.Equals(r.MediaType, "Removable", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(r.MediaType, "Network", StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.Tier)
            .ToDictionary(g => g.Key, g => g
                .OrderByDescending(r => r.FreeBytes ?? 0)
                .First().Id);

        var requests = new List<MigrationRequest>();
        foreach (var move in plan.Proposals.Take(batchSize))
        {
            if (targetByTier.TryGetValue(move.ToTier, out var repoId))
                requests.Add(new MigrationRequest(move.VarFileId, repoId));
        }
        return requests;
    }
}
