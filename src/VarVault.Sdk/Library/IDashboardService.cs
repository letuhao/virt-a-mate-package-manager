namespace VarVault.Sdk.Library;

/// <summary>Per-tier storage utilization for the dashboard.</summary>
public sealed record TierUtilization(int Tier, long UsedBytes, long CapacityBytes, int RepositoryCount);

/// <summary>The dashboard header/summary tiles, composed from catalog + repositories. (16-checklist BE-N1.)</summary>
public sealed record DashboardSummary(
    int TotalPackages,
    long TotalBytes,
    int RepositoryCount,
    int OfflineRepositoryCount,
    int ActiveInGame,
    int HotCount,
    int WarmCount,
    int ColdCount,
    int MissingDepsCount,
    IReadOnlyList<TierUtilization> Tiers);

/// <summary>
/// BE-N1 · Read facade for the Dashboard screen: totals, classification counts, per-tier storage, and
/// attention counts (missing deps, offline repos). (16-checklist BE-N1.)
/// </summary>
public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
}
