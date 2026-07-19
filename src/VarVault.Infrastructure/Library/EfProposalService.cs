using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Entities;
using VarVault.Domain.Safety;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N8 · Proposal aggregator. Builds propose-only actions from the tiering/reclaim/health facades plus a
/// stale query, and on approve dispatches to the matching runner. (16-checklist BE-N8.)
/// </summary>
public sealed class EfProposalService(
    VarVaultDbContext db,
    ITieringService tiering,
    IReclaimService reclaim,
    IHealthService health,
    IMigrationService migration,
    ITrashService trash) : IProposalService
{
    public async Task<IReadOnlyList<Proposal>> ListAsync(CancellationToken cancellationToken = default)
    {
        var proposals = new List<Proposal>();

        // Rebalance — one summary proposal from the migration plan.
        var plan = await tiering.BuildPlanAsync(cancellationToken).ConfigureAwait(false);
        if (plan.Proposals.Count > 0)
            proposals.Add(new Proposal(
                $"rebalance", ProposalKind.Rebalance, $"Rebalance {plan.Proposals.Count} misplaced vars",
                $"{plan.Proposals.Count} moves · {plan.ExcludedCount} excluded", 0,
                plan.Proposals.Select(p => p.VarFileId).ToList()));

        // Dedup — one proposal per exact duplicate group (trash all but the first).
        foreach (var g in await reclaim.ExactGroupsAsync(cancellationToken).ConfigureAwait(false))
        {
            var ids = g.Copies.Select(c => c.VarFileId).ToList();
            var reclaimBytes = g.Copies.Skip(1).Sum(c => c.SizeBytes);
            proposals.Add(new Proposal(
                $"dedup:{g.IdentityKey}", ProposalKind.Dedup, $"Remove {ids.Count - 1} duplicate copies of {g.IdentityKey}",
                "content-signature matched · verified before delete", reclaimBytes, ids));
        }

        // Encoding fix — one proposal per codepage group.
        foreach (var eg in await health.EncodingGroupsAsync(cancellationToken).ConfigureAwait(false))
        {
            var brokenIds = await db.VarFiles
                .Where(v => v.DetectedCodepage == eg.Codepage
                            && (v.EncodingHealth == EncodingHealth.NeedsFix || v.EncodingHealth == EncodingHealth.PartiallyBroken))
                .Select(v => v.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            proposals.Add(new Proposal(
                $"encoding:{eg.Codepage}", ProposalKind.EncodingFix, $"Fix {eg.Count} {eg.Codepage} vars → UTF-8",
                "new UTF-8 var · original retained", 0, brokenIds, eg.Codepage));
        }

        // Retire stale — old versions with a newer sibling and no reverse dependents.
        var stale = await StaleCanonicalVarIdsAsync(cancellationToken).ConfigureAwait(false);
        if (stale.Count > 0)
            proposals.Add(new Proposal(
                "stale", ProposalKind.RetireStale, $"Retire {stale.Count} superseded old versions",
                "not latest & not depended-on · → trash", 0, stale));

        return proposals;
    }

    public async Task<ProposalActionResult> ApproveAsync(Proposal proposal, CancellationToken cancellationToken = default)
    {
        switch (proposal.Kind)
        {
            case ProposalKind.Dedup:
                if (proposal.VarFileIds.Count < 2) return new ProposalActionResult(true, "nothing to reclaim");
                var r = await reclaim.TrashRedundantAsync(proposal.VarFileIds[0], proposal.VarFileIds.Skip(1).ToList(), cancellationToken).ConfigureAwait(false);
                return new ProposalActionResult(r.Blocked == 0, $"trashed {r.Trashed}, blocked {r.Blocked}");

            case ProposalKind.EncodingFix:
                int fixedCount = 0;
                foreach (var id in proposal.VarFileIds)
                    if ((await health.FixAsync(id, cancellationToken).ConfigureAwait(false)).IsSuccess)
                        fixedCount++;
                return new ProposalActionResult(fixedCount > 0, $"fixed {fixedCount}/{proposal.VarFileIds.Count}");

            case ProposalKind.Rebalance:
                var plan = await tiering.BuildPlanAsync(cancellationToken).ConfigureAwait(false);
                var moves = await MapMovesAsync(plan, cancellationToken).ConfigureAwait(false);
                var mr = await migration.RunAsync(moves, cancellationToken).ConfigureAwait(false);
                return new ProposalActionResult(mr.Failed == 0, $"moved {mr.Moved}, failed {mr.Failed}");

            case ProposalKind.RetireStale:
                int retired = 0;
                foreach (var path in await PathsForAsync(proposal.VarFileIds, cancellationToken).ConfigureAwait(false))
                    if ((await trash.TrashAsync(path, "superseded old version", cancellationToken).ConfigureAwait(false)).IsSuccess)
                        retired++;
                return new ProposalActionResult(true, $"retired {retired}");

            default:
                return new ProposalActionResult(false, "unknown proposal");
        }
    }

    public Task<ProposalActionResult> RejectAsync(Proposal proposal, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProposalActionResult(true, "rejected"));

    private async Task<List<MigrationRequest>> MapMovesAsync(TierMigrationPlan plan, CancellationToken cancellationToken)
    {
        var repos = await db.Repositories.Where(r => r.IsOnline).Select(r => new { r.Id, r.Tier }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var moves = new List<MigrationRequest>();
        foreach (var p in plan.Proposals)
        {
            var target = repos.FirstOrDefault(r => r.Tier == p.ToTier);
            if (target is not null)
                moves.Add(new MigrationRequest(p.VarFileId, target.Id));
        }
        return moves;
    }

    private async Task<List<long>> StaleCanonicalVarIdsAsync(CancellationToken cancellationToken)
    {
        var packages = await db.Packages
            .Select(p => new { p.Id, p.Creator, p.PackageName, p.VersionSort, p.ReverseDependentCount, p.CanonicalVarFileId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var latestByFamily = packages
            .GroupBy(p => (p.Creator, p.PackageName))
            .ToDictionary(g => g.Key, g => g.Max(p => p.VersionSort));

        return packages
            .Where(p => p.ReverseDependentCount == 0
                        && p.CanonicalVarFileId is not null
                        && p.VersionSort < latestByFamily[(p.Creator, p.PackageName)])
            .Select(p => p.CanonicalVarFileId!.Value)
            .ToList();
    }

    private async Task<List<string>> PathsForAsync(IReadOnlyList<long> varFileIds, CancellationToken cancellationToken)
    {
        var rows = await db.VarFiles
            .Where(v => varFileIds.Contains(v.Id))
            .Select(v => new { v.RelativePath, v.Repository!.MountPath })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => Path.Combine(r.MountPath, r.RelativePath)).ToList();
    }
}
