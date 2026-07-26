using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Analyzer;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N3 · Migration facade: persists each requested move as a <see cref="MigrationJob"/> and runs it
/// through <see cref="MigrationRunner"/> (copy → verify → rename → trash source). (16-checklist BE-N3.)
/// </summary>
public sealed class EfMigrationService(VarVaultDbContext db, MigrationRunner runner) : IMigrationService
{
    public async Task<MigrationRunResult> RunAsync(IReadOnlyList<MigrationRequest> moves, CancellationToken cancellationToken = default)
    {
        int moved = 0, failed = 0;
        foreach (var move in moves)
        {
            var varFile = await db.VarFiles.FirstOrDefaultAsync(v => v.Id == move.VarFileId, cancellationToken).ConfigureAwait(false);
            if (varFile is null) { failed++; continue; }

            var job = new MigrationJob
            {
                VarFileId = varFile.Id,
                SourceRepositoryId = varFile.RepositoryId,
                TargetRepositoryId = move.TargetRepositoryId,
                State = MigrationState.Planned,
            };
            db.MigrationJobs.Add(job);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var state = await runner.RunAsync(job.Id, cancellationToken).ConfigureAwait(false);
            if (state == MigrationState.Done) moved++; else failed++;
        }
        return new MigrationRunResult(moved, failed);
    }

    public async Task<MigrationRunResult> RebalanceOntoRepositoryAsync(Guid targetRepositoryId, CancellationToken cancellationToken = default)
    {
        var target = await db.Repositories.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == targetRepositoryId, cancellationToken).ConfigureAwait(false);
        if (target is null || !target.IsOnline || !target.IsEnabled)
            return new MigrationRunResult(0, 0);

        var copyCounts = await db.VarFiles.AsNoTracking()
            .Where(v => v.PackageId != null)
            .GroupBy(v => v.PackageId!.Value)
            .Select(g => new { PackageId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PackageId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        var listByPkg = await db.PackageListItems.AsNoTracking()
            .ToDictionaryAsync(x => x.PackageId, cancellationToken)
            .ConfigureAwait(false);

        var rows = await (
            from v in db.VarFiles.AsNoTracking()
            join r in db.Repositories.AsNoTracking() on v.RepositoryId equals r.Id
            where v.RepositoryId != targetRepositoryId && v.PackageId != null
            select new { v.Id, PackageId = v.PackageId!.Value, CurrentTier = r.Tier, Online = r.IsOnline })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var candidates = new List<MigrationCandidate>();
        foreach (var row in rows)
        {
            if (!listByPkg.TryGetValue(row.PackageId, out var item))
                continue;
            candidates.Add(new MigrationCandidate(
                row.Id,
                row.CurrentTier,
                item.Class,
                IsOnline: row.Online,
                IsSingleCopy: copyCounts.GetValueOrDefault(row.PackageId, 1) <= 1));
        }

        var ids = RebalancePlanner.CandidatesForNewTier(candidates, target.Tier);
        var moves = ids.Select(id => new MigrationRequest(id, targetRepositoryId)).ToList();
        return moves.Count == 0
            ? new MigrationRunResult(0, 0)
            : await RunAsync(moves, cancellationToken).ConfigureAwait(false);
    }
}
