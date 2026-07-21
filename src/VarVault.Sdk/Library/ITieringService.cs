using VarVault.Sdk.Paging;

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

/// <summary>One class→desired-tier placement rule (the policy that drives misplacement + migration). (24-checklist A6.)</summary>
public sealed record TierPolicyEntry(string StorageClass, int DesiredTier);

/// <summary>The active placement policy (read-only): how each usage class maps to a storage tier. (24-checklist A6.)</summary>
public sealed record TierPolicy(IReadOnlyList<TierPolicyEntry> Placements);

/// <summary>A superseded, cold version — a newer version of the same identity exists. (24-checklist A7.)</summary>
public sealed record StaleVersion(long PackageId, string VarName, string StorageClass, long SizeBytes);

/// <summary>
/// BE-N2 · Tiering read/plan facade over PlacementPolicy + MigrationPlanner. Surfaces class counts, the
/// misplaced list, and a propose-only migration plan (never executes). (16-checklist BE-N2.)
/// </summary>
public interface ITieringService
{
    Task<TierClassCounts> ClassCountsAsync(CancellationToken cancellationToken = default);
    async Task<PageResult<MisplacedItem>> MisplacedPageAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        var all = await MisplacedAsync(cancellationToken).ConfigureAwait(false);
        var page = request.Normalize();
        return new PageResult<MisplacedItem>(all.Skip(page.Skip).Take(page.SafePageSize).ToList(), all.Count, page.SafePageNumber, page.SafePageSize);
    }
    Task<IReadOnlyList<MisplacedItem>> MisplacedAsync(CancellationToken cancellationToken = default);
    Task<TierMigrationPlan> BuildPlanAsync(CancellationToken cancellationToken = default);

    /// <summary>The active class→tier placement policy (read-only display). (24-checklist A6.)</summary>
    Task<TierPolicy> PolicyAsync(CancellationToken cancellationToken = default);

    /// <summary>Superseded + cold versions (a newer version of the same identity exists). (24-checklist A7.)</summary>
    async Task<PageResult<StaleVersion>> StaleVersionsPageAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        var all = await StaleVersionsAsync(cancellationToken).ConfigureAwait(false);
        var page = request.Normalize();
        return new PageResult<StaleVersion>(all.Skip(page.Skip).Take(page.SafePageSize).ToList(), all.Count, page.SafePageNumber, page.SafePageSize);
    }
    Task<IReadOnlyList<StaleVersion>> StaleVersionsAsync(CancellationToken cancellationToken = default);
}
