using VarVault.Sdk.Library;

namespace VarVault.App.Services;

/// <summary>A single poll of the shell's live counters + status strings. (GA-3/GA-4.)</summary>
public sealed record ShellLiveSnapshot(
    int ProposalCount,
    int HealthCount,
    int MissingCount,
    string? TierSummary,
    string? IndexStatus);

/// <summary>
/// GA-3/GA-4 · Supplies the shell's rail badges (proposals/health/missing) and log-dock status
/// (tier fill, index progress) from SDK read services. Implemented in the composition root so the shell
/// depends only on this seam. (18-gap GA-3/GA-4.)
/// </summary>
public interface IShellLiveFeeds
{
    Task<ShellLiveSnapshot> SnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default live-feeds source: badges from proposals/health/missing services, tier summary from the
/// dashboard service, index status from the job queue's active indexing job.
/// </summary>
public sealed class ShellLiveFeeds(
    IProposalService proposals,
    IHealthService health,
    IMissingDepsQuery missing,
    IDashboardService dashboard,
    Sdk.Threading.IJobQueue jobQueue) : IShellLiveFeeds
{
    public async Task<ShellLiveSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        var pending = await proposals.ListAsync(cancellationToken).ConfigureAwait(false);
        var encoding = await health.EncodingGroupsAsync(cancellationToken).ConfigureAwait(false);
        var miss = await missing.GetMissingAsync(cancellationToken).ConfigureAwait(false);
        var summary = await dashboard.GetSummaryAsync(cancellationToken).ConfigureAwait(false);

        var tiers = summary.Tiers;
        var tierSummary = tiers.Count == 0
            ? null
            : "Storage " + string.Join(" · ", tiers.Select(t =>
                $"T{t.Tier} {(t.CapacityBytes == 0 ? 0 : t.UsedBytes * 100 / t.CapacityBytes)}%"));

        var indexJob = jobQueue.Active.FirstOrDefault(j => j.Name.Contains("Index", StringComparison.OrdinalIgnoreCase));
        var indexStatus = indexJob is null
            ? null
            : $"{indexJob.Name} · {indexJob.Progress.Done}/{indexJob.Progress.Total}";

        return new ShellLiveSnapshot(
            ProposalCount: pending.Count,
            HealthCount: encoding.Sum(g => g.Count),
            MissingCount: miss.Count,
            TierSummary: tierSummary,
            IndexStatus: indexStatus);
    }
}
