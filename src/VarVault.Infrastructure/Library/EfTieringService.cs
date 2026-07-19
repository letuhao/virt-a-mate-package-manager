using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Analyzer;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N2 · Tiering facade. Class counts and misplaced list from the read model; a propose-only migration
/// plan built from per-copy candidates via <see cref="MigrationPlanner"/> (single-copy/offline/unsafe
/// targets excluded). (16-checklist BE-N2.)
/// </summary>
public sealed class EfTieringService(VarVaultDbContext db, IRepositoryService repositories) : ITieringService
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

    public async Task<IReadOnlyList<MisplacedItem>> MisplacedAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await db.PackageListItems
            .Where(x => x.ActualTierMin != null)
            .Select(x => new { x.PackageId, x.VarName, x.Class, x.ActualTierMin, x.TotalSize })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<MisplacedItem>();
        foreach (var c in candidates)
        {
            var actual = c.ActualTierMin!.Value;
            if (!PlacementPolicy.IsMisplaced(c.Class, actual))
                continue;
            var desired = PlacementPolicy.DesiredTier(c.Class);
            var why = desired < actual ? $"{c.Class} sitting on slower tier" : $"{c.Class} wasting faster tier";
            result.Add(new MisplacedItem(c.PackageId, c.VarName, c.Class.ToString(), actual, desired, c.TotalSize, why));
        }
        return result;
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
            .Select(v => new { VarFileId = v.Id, v.RepositoryId, PackageId = v.PackageId!.Value })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Join per-copy facts: repo tier/online + package class + single-copy.
        var listByPkg = await db.PackageListItems.ToDictionaryAsync(x => x.PackageId, cancellationToken).ConfigureAwait(false);
        var candidates = new List<MigrationCandidate>();
        foreach (var r in rows)
        {
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
        return new TierMigrationPlan(
            plan.Proposals.Select(p => new TierMoveProposal(p.VarFileId, p.FromTier, p.ToTier)).ToList(),
            plan.Excluded.Count);
    }
}
