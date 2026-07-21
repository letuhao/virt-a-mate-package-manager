using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>Persists dirty package IDs so derived refresh survives crashes. (A15.)</summary>
public sealed class DurableDirtySet(VarVaultDbContext db, IClock clock) : IDurableDirtySet
{
    public async Task MarkAsync(long packageId, string reason = "ingest", CancellationToken cancellationToken = default)
    {
        var existing = await db.DirtyPackages.FindAsync([packageId], cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            db.DirtyPackages.Add(new DirtyPackage
            {
                PackageId = packageId,
                MarkedAt = clock.UtcNow.UtcDateTime,
                Reason = reason,
            });
        }
        else
        {
            existing.MarkedAt = clock.UtcNow.UtcDateTime;
            existing.Reason = reason;
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
    }

    public async Task MarkManyAsync(IEnumerable<long> packageIds, string reason = "ingest", CancellationToken cancellationToken = default)
    {
        foreach (var id in packageIds.Distinct())
            await MarkAsync(id, reason, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<long>> DrainBatchAsync(int take, CancellationToken cancellationToken = default)
    {
        var batch = await db.DirtyPackages
            .OrderBy(d => d.MarkedAt)
            .Take(take)
            .Select(d => d.PackageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (batch.Count == 0)
            return batch;

        await db.DirtyPackages.Where(d => batch.Contains(d.PackageId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        return batch;
    }
}
