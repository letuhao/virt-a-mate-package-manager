namespace VarVault.Domain.Indexing;

/// <summary>
/// A <b>packed</b> thumbnail store keyed by PackageId — a few blob files / one DB, not 700k loose
/// files (review Perf-MED12). Lives on the fastest tier. Implemented by Infrastructure. (Checklist 1.33.)
/// </summary>
public interface IThumbnailStore
{
    Task PutAsync(long packageId, byte[] jpeg, CancellationToken cancellationToken = default);
    Task<byte[]?> GetAsync(long packageId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(long packageId, CancellationToken cancellationToken = default);
}
