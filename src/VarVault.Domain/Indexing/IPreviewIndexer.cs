namespace VarVault.Domain.Indexing;

/// <summary>
/// Pass-2 of staged indexing: extract and store previews for packages whose base rows pass-1 already
/// wrote. Runs <b>after</b> the catalog is browsable, so the gallery shows type placeholders immediately
/// and thumbnails fill in as this completes. Implemented by Infrastructure (zip + thumbnail store).
/// (Checklist 1.23/1.32.)
/// </summary>
public interface IPreviewIndexer
{
    /// <summary>Extract + store previews for the given packages, returning how many were stored.</summary>
    Task<int> BuildPreviewsAsync(IReadOnlyCollection<long> packageIds, CancellationToken cancellationToken = default);
}
