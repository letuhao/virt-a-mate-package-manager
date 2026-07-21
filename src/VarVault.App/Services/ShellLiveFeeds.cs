using Microsoft.Extensions.DependencyInjection;
using VarVault.Sdk.Indexer;
using VarVault.Sdk.Library;

namespace VarVault.App.Services;

/// <summary>A single poll of the shell's live counters + status strings. (GA-3/GA-4.)</summary>
public sealed record ShellLiveSnapshot(
    int ProposalCount,
    int HealthCount,
    int MissingCount,
    string? TierSummary,
    string? IndexStatus,
    int TotalPackages = 0);

public interface IShellLiveFeeds
{
    Task<ShellLiveSnapshot> SnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Lightweight live feeds: dashboard aggregates + indexer worker status. Avoids rebuilding full
/// proposal/dedup graphs every tick (that caused GUI lag during 5 TB scans). (A12 redesign.)
/// </summary>
public sealed class ShellLiveFeeds(
    IServiceScopeFactory scopeFactory,
    IIndexerClient? indexer = null) : IShellLiveFeeds
{
    private static readonly ShellLiveSnapshot Empty = new(0, 0, 0, null, null);
    private int _tick;
    private int _cachedProposalCount;
    private int _cachedHealthCount;

    public async Task<ShellLiveSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var dashboard = sp.GetRequiredService<IDashboardService>();
            var summary = await dashboard.GetSummaryAsync(cancellationToken).ConfigureAwait(false);

            var tiers = summary.Tiers;
            var tierSummary = tiers.Count == 0
                ? null
                : "Storage " + string.Join(" · ", tiers.Select(t =>
                    $"T{t.Tier} {(t.CapacityBytes == 0 ? 0 : t.UsedBytes * 100 / t.CapacityBytes)}%"));

            string? indexStatus = null;
            var client = indexer ?? sp.GetService<IIndexerClient>();
            if (client is not null)
            {
                var status = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                if (status.IsSuccess && status.Value.State is not Sdk.Indexer.IndexerJobState.Idle
                    and not Sdk.Indexer.IndexerJobState.Completed)
                {
                    var s = status.Value;
                    indexStatus = s.PhaseMessage is { Length: > 0 }
                        ? (s.Total > 0 ? $"{s.PhaseMessage} · {s.Done:N0}/{s.Total:N0}" : s.PhaseMessage)
                        : s.State.ToString();
                }
            }

            // Expensive badge sources only every 4th poll (~8–10s at 2s timer).
            // Cache last values so intermediate ticks do not flash badges to zero.
            var tick = Interlocked.Increment(ref _tick);
            if (tick % 4 == 1)
            {
                try
                {
                    var proposals = sp.GetService<IProposalService>();
                    if (proposals is not null)
                        _cachedProposalCount = (await proposals.ListAsync(cancellationToken).ConfigureAwait(false)).Count;
                    var health = sp.GetService<IHealthService>();
                    if (health is not null)
                        _cachedHealthCount = (await health.EncodingGroupsAsync(cancellationToken).ConfigureAwait(false)).Sum(g => g.Count);
                }
                catch
                {
                    // best-effort badges — keep previous cache
                }
            }

            return new ShellLiveSnapshot(
                ProposalCount: _cachedProposalCount,
                HealthCount: _cachedHealthCount,
                MissingCount: summary.MissingDepsCount,
                TierSummary: tierSummary,
                IndexStatus: indexStatus,
                TotalPackages: summary.TotalPackages);
        }
        catch (ObjectDisposedException)
        {
            return Empty;
        }
    }
}
