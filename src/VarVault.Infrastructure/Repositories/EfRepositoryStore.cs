using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Repositories;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Repositories;

/// <summary>EF implementation of <see cref="IRepositoryStore"/>.</summary>
public sealed class EfRepositoryStore(VarVaultDbContext db) : IRepositoryStore
{
    public async Task<IReadOnlyList<Repository>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Repositories.AsNoTracking().OrderBy(r => r.Tier).ThenBy(r => r.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Repository?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Repositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

    public async Task AddAsync(Repository repository, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(repository);
        await db.Repositories.AddAsync(repository, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        if (repo is null)
            return false;

        // DB-only: deleting the row cascades (FK ON DELETE CASCADE, foreign_keys=ON) to this repo's VarFiles and
        // their dependents (dependencies, content items, activation links). The .var files on disk are untouched.
        db.Repositories.Remove(repo);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Packages left with no var files anywhere are now orphans — prune them (cascades their aggregate rows).
        var orphans = await db.Packages.Where(p => !p.VarFiles.Any())
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (orphans.Count > 0)
        {
            db.Packages.RemoveRange(orphans);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
