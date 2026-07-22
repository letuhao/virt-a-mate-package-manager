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

    /// <summary>Content-item wall thumbnails keyed by <see cref="ContentItem"/> id (negative shard key).</summary>
    Task PutContentAsync(long contentItemId, byte[] jpeg, CancellationToken cancellationToken = default);
    Task<byte[]?> GetContentAsync(long contentItemId, CancellationToken cancellationToken = default);

    /// <summary>Higher-res focus viewer image for one content item (separate table; not the 384px wall cache).</summary>
    Task PutFocusContentAsync(long contentItemId, byte[] jpeg, CancellationToken cancellationToken = default);
    Task<byte[]?> GetFocusContentAsync(long contentItemId, CancellationToken cancellationToken = default);
}
