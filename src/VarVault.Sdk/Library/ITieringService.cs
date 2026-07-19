namespace VarVault.Sdk.Library;

/// <summary>Hot/warm/cold package counts.</summary>
public sealed record TierClassCounts(int Hot, int Warm, int Cold);

/// <summary>A package whose actual tier doesn't match its class's desired tier.</summary>
public sealed record MisplacedItem(
    long PackageId, string VarName, string StorageClass, int CurrentTier, int DesiredTier, long SizeBytes, string Why);

/// <summary>A proposed tier move (never executed by the planner).</summary>
public sealed record TierMoveProposal(long VarFileId, int FromTier, int ToTier);

/// <summary>A migration plan: proposals + how many candidates were excluded (single-copy/offline/unsafe).</summary>
public sealed record TierMigrationPlan(IReadOnlyList<TierMoveProposal> Proposals, int ExcludedCount);

/// <summary>
/// BE-N2 · Tiering read/plan facade over PlacementPolicy + MigrationPlanner. Surfaces class counts, the
/// misplaced list, and a propose-only migration plan (never executes). (16-checklist BE-N2.)
/// </summary>
public interface ITieringService
{
    Task<TierClassCounts> ClassCountsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MisplacedItem>> MisplacedAsync(CancellationToken cancellationToken = default);
    Task<TierMigrationPlan> BuildPlanAsync(CancellationToken cancellationToken = default);
}
