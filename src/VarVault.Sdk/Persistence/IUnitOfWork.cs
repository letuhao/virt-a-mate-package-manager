namespace VarVault.Sdk.Persistence;

/// <summary>
/// Commits a set of catalog changes atomically. Obtained and used only inside an
/// <see cref="Threading.IWriteQueue"/> write (never concurrently), so the single-writer
/// contract holds. Reads use query services directly, not this.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
