using Microsoft.Extensions.DependencyInjection;
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
/// <para>
/// Each poll runs in its own DI scope so its scoped <c>VarVaultDbContext</c> is never shared with a screen's
/// concurrent read (EF Core <c>DbContext</c> is not thread-safe). The 750 ms shell timer and a user screen-load
/// can otherwise interleave on one context → intermittent "second operation started on this context". (24-checklist B1.)
/// </para>
/// </summary>
public sealed class ShellLiveFeeds(
    IServiceScopeFactory scopeFactory,
    Sdk.Threading.IJobQueue jobQueue) : IShellLiveFeeds
{
    public async Task<ShellLiveSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var proposals = sp.GetRequiredService<IProposalService>();
        var health = sp.GetRequiredService<IHealthService>();
        var missing = sp.GetRequiredService<IMissingDepsQuery>();
        var dashboard = sp.GetRequiredService<IDashboardService>();

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
            IndexStatus: indexStatus,
            TotalPackages: summary.TotalPackages);
    }
}
