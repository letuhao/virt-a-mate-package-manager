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

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
