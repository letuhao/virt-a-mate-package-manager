using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Analyzer;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;
using VarVault.Sdk.Repositories;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N2 · Tiering facade. Class counts and misplaced list from the read model; a propose-only migration
/// plan built from per-copy candidates via <see cref="MigrationPlanner"/> (single-copy/offline/unsafe
/// targets excluded). (16-checklist BE-N2.)
/// </summary>
public sealed class EfTieringService(
    VarVaultDbContext db,
    IRepositoryService repositories,
    FreeSpaceLedger? ledger = null) : ITieringService
{
    public async Task<TierClassCounts> ClassCountsAsync(CancellationToken cancellationToken = default)
    {
        var byClass = await db.PackageListItems
            .GroupBy(x => x.Class)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken)
            .ConfigureAwait(false);
        return new TierClassCounts(
            byClass.GetValueOrDefault(ContentClass.Hot),
            byClass.GetValueOrDefault(ContentClass.Warm),
            byClass.GetValueOrDefault(ContentClass.Cold));
    }

    public async Task<PageResult<MisplacedItem>> MisplacedPageAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        var page = request.Normalize();
        // Desired tiers are 1-based (Hot→1, Warm→2, Cold→3) — must match PlacementPolicy / TierPolicy.
        var query = db.PackageListItems.AsNoTracking()
            .Where(x => x.ActualTierMin != null)
            .Select(x => new
            {
                x.PackageId,
                x.VarName,
                x.Class,
                Actual = x.ActualTierMin!.Value,
                Desired = x.Class == ContentClass.Hot ? 1 : x.Class == ContentClass.Warm ? 2 : 3,
                x.TotalSize,
            })
            .Where(x => x.Actual != x.Desired);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(x => x.TotalSize)
            .ThenBy(x => x.VarName)
            .ThenBy(x => x.PackageId)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Select(x => new MisplacedItem(
            x.PackageId,
            x.VarName,
            x.Class.ToString(),
            x.Actual,
            x.Desired,
            x.TotalSize,
            x.Desired < x.Actual ? $"{x.Class} sitting on slower tier" : $"{x.Class} wasting faster tier")).ToList();

        return new PageResult<MisplacedItem>(items, total, page.SafePageNumber, page.SafePageSize);
    }

    public async Task<IReadOnlyList<MisplacedItem>> MisplacedAsync(CancellationToken cancellationToken = default)
    {
        return (await MisplacedPageAsync(new PageRequest(1, 100), cancellationToken).ConfigureAwait(false)).Items;
    }

    public async Task<TierMigrationPlan> BuildPlanAsync(CancellationToken cancellationToken = default)
    {
        var repos = await repositories.ListAsync(cancellationToken).ConfigureAwait(false);
        var repoById = repos.ToDictionary(r => r.Id);

        // A tier is a safe destination if some online, fixed (non-removable/network) repo serves it.
        var safeTiers = repos
            .Where(r => r.IsOnline && r.MediaType is not ("Removable" or "Network"))
            .Select(r => r.Tier)
            .ToHashSet();

        var rows = await db.VarFiles
            .Where(v => v.PackageId != null)
            .Select(v => new { VarFileId = v.Id, v.RepositoryId, PackageId = v.PackageId!.Value, v.SizeBytes })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Join per-copy facts: repo tier/online + package class + single-copy.
        var listByPkg = await db.PackageListItems.ToDictionaryAsync(x => x.PackageId, cancellationToken).ConfigureAwait(false);
        var candidates = new List<MigrationCandidate>();
        var sizeByVar = new Dictionary<long, long>();
        foreach (var r in rows)
        {
            sizeByVar[r.VarFileId] = r.SizeBytes;
            if (!repoById.TryGetValue(r.RepositoryId, out var repo) || !listByPkg.TryGetValue(r.PackageId, out var item))
                continue;
            candidates.Add(new MigrationCandidate(
                VarFileId: r.VarFileId,
                CurrentTier: repo.Tier,
                StorageClass: item.Class,
                IsOnline: repo.IsOnline,
                IsSingleCopy: item.IsSingleCopy));
        }

        var plan = MigrationPlanner.Plan(candidates, tier => safeTiers.Contains(tier));

        // Soft capacity filter: skip proposals that won't fit the best online dest on ToTier.
        var entityRepos = await db.Repositories.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var proposals = new List<TierMoveProposal>();
        var capacityExcluded = 0;
        foreach (var p in plan.Proposals)
        {
            var size = sizeByVar.GetValueOrDefault(p.VarFileId);
            var dest = entityRepos
                .Where(r => r.Tier == p.ToTier && r.IsOnline && r.IsEnabled
                            && r.MediaType is not MediaType.Removable and not MediaType.Network)
                .OrderByDescending(r => r.FreeBytes ?? 0)
                .FirstOrDefault();
            if (dest is null)
            {
                capacityExcluded++;
                continue;
            }
            // Soft: only exclude when free space is known and insufficient (after in-flight ledger).
            // Unknown FreeBytes → keep proposal; runner prefers live DriveInfo at execute time.
            if (dest.FreeBytes is { } free)
            {
                var effective = free - (ledger?.Reserved(dest.Id) ?? 0);
                if (!PlacementCapacity.HasRoom(effective, dest.MinFreeBytes, Math.Max(1, size)))
                {
                    capacityExcluded++;
                    continue;
                }
            }
            proposals.Add(new TierMoveProposal(p.VarFileId, p.FromTier, p.ToTier));
        }

        return new TierMigrationPlan(proposals, plan.Excluded.Count + capacityExcluded);
    }

    public Task<TierPolicy> PolicyAsync(CancellationToken cancellationToken = default) =>
        // Read-only view of the active placement policy the engine actually uses. (24-checklist A6.)
        Task.FromResult(new TierPolicy(
        [
            new(nameof(ContentClass.Hot), PlacementPolicy.DesiredTier(ContentClass.Hot)),
            new(nameof(ContentClass.Warm), PlacementPolicy.DesiredTier(ContentClass.Warm)),
            new(nameof(ContentClass.Cold), PlacementPolicy.DesiredTier(ContentClass.Cold)),
        ]));

    public async Task<PageResult<StaleVersion>> StaleVersionsPageAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        var page = request.Normalize();
        var items = db.PackageListItems.AsNoTracking();
        var latestByFamily = db.Packages.AsNoTracking()
            .GroupBy(p => new { p.Creator, p.PackageName })
            .Select(g => new { g.Key.Creator, g.Key.PackageName, MaxSort = g.Max(x => x.VersionSort) });

        var query = items
            .Join(db.Packages.AsNoTracking(), i => i.PackageId, p => p.Id, (i, p) => new { i, p })
            .Join(latestByFamily,
                x => new { x.p.Creator, x.p.PackageName },
                g => new { g.Creator, g.PackageName },
                (x, g) => new { x.i, x.p, g.MaxSort })
            .Where(x => x.i.Class == ContentClass.Cold && x.p.VersionSort < x.MaxSort);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(x => x.i.TotalSize)
            .ThenBy(x => x.i.VarName)
            .ThenBy(x => x.i.PackageId)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(x => new StaleVersion(x.i.PackageId, x.i.VarName, x.i.Class.ToString(), x.i.TotalSize))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PageResult<StaleVersion>(rows, total, page.SafePageNumber, page.SafePageSize);
    }

    public async Task<IReadOnlyList<StaleVersion>> StaleVersionsAsync(CancellationToken cancellationToken = default)
    {
        return (await StaleVersionsPageAsync(new PageRequest(1, 100), cancellationToken).ConfigureAwait(false)).Items;
    }
}
