using VarVault.Domain.Entities;

namespace VarVault.Domain.Analyzer;

/// <summary>
/// Records usage signals and recomputes hot/warm/cold classifications from them. Every app-performed
/// activate/load appends a <see cref="UsageEvent"/>; recompute blends the windowed counts with
/// centrality via <see cref="UsageScoring"/> and updates each package's <see cref="UsageStat"/> and the
/// read-model class. Implemented by Infrastructure. (Checklist 5.1, BE-A1/A6.)
/// </summary>
public interface IUsageAnalyzer
{
    /// <summary>Append a usage event for a package. (5.1, BE-A1.)</summary>
    Task RecordAsync(long packageId, UsageKind kind, CancellationToken cancellationToken = default);

    /// <summary>Recompute classifications for packages that have usage events. Returns the count recomputed.</summary>
    Task<int> RecomputeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Compact usage events older than <paramref name="olderThanDays"/> into per-package rollup counts
    /// (preserving <c>UseCountTotal</c>) and delete the raw events. Returns the number compacted.
    /// (5.3, BE-A7.)
    /// </summary>
    Task<int> CompactAsync(int olderThanDays = 90, CancellationToken cancellationToken = default);
}
