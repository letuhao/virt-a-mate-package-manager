namespace VarVault.Sdk.Library;

/// <summary>Sort orders for the library grid (each backed by a composite index). (Data-arch §5.8.)</summary>
public enum LibrarySort
{
    Name = 0,
    Creator = 1,
    Size = 2,
    LastUsed = 3,
    Class = 4,
}

/// <summary>A filtered/sorted/paged query over the materialized library read model.</summary>
public sealed record LibraryQuery(
    int Skip = 0,
    int Take = 100,
    string? Creator = null,
    string? SearchText = null,
    bool FavoritesOnly = false,
    bool MissingDepsOnly = false,
    LibrarySort Sort = LibrarySort.Name,
    bool Descending = false);

/// <summary>One row the library grid binds to (SDK-safe; no EF entities). </summary>
public sealed record PackageListEntry(
    long PackageId,
    string VarName,
    string Creator,
    string PackageName,
    string VersionToken,
    string PrimaryType,
    long TotalSize,
    int OnlineInstanceCount,
    int TotalInstanceCount,
    bool IsSingleCopy,
    bool IsFavorite,
    string StorageClass,
    bool HasMissingDeps,
    DateTime? LastUsedAt);

/// <summary>A page of results plus the total match count (for the scrollbar / counts).</summary>
public sealed record LibraryPage(IReadOnlyList<PackageListEntry> Items, int TotalCount);

/// <summary>
/// Reads the materialized <c>PackageListItem</c> table for the library UI — filtered, sorted, and paged.
/// The public boundary the view-models bind to. (Checklist 1.38/1.41/1.46.)
/// </summary>
public interface ILibraryQueryService
{
    Task<LibraryPage> GetPageAsync(LibraryQuery query, CancellationToken cancellationToken = default);

    /// <summary>Distinct creators for the searchable creator combobox. (1.46)</summary>
    Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken cancellationToken = default);
}
