using VarVault.Domain.Entities;

namespace VarVault.Domain.Repositories;

/// <summary>
/// Persistence seam for repository rows (Domain-entity based), implemented by Infrastructure over EF
/// and consumed by the Repositories module — so the module never binds to EF. (Architecture doc 11.)
/// </summary>
public interface IRepositoryStore
{
    Task<IReadOnlyList<Repository>> ListAsync(CancellationToken cancellationToken = default);
    Task<Repository?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Repository repository, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
