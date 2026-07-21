using System.Collections.Generic;
using System.Linq;

namespace VarVault.Sdk.Library;

/// <summary>Sort orders for the library grid (each backed by a composite index). (Data-arch §5.8.)</summary>
public enum LibrarySort
{
    Name = 0,
    Creator = 1,
    Size = 2,
    LastUsed = 3,
    Class = 4,
    Added = 5,
    Installed = 6,
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
    bool Descending = false,
    // Facet extensions (BE-N12).
    string? PackageName = null,
    bool InstalledOnly = false,
    bool SingleCopyOnly = false,
    long? TagId = null,
    IReadOnlyList<string>? Types = null,
    IReadOnlyList<int>? Tiers = null);

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
    DateTime? LastUsedAt,
    int? Tier = null,
    IReadOnlyDictionary<string, int>? ContentCounts = null,
    DateTime? AddedAt = null,
    DateTime? InstalledAt = null,
    int DependencyCount = 0,
    bool IsActive = false)
{
    /// <summary>Compact per-content-type breakdown for the grid/detail, e.g. "Sc 3  Lk 1  Pl 2". (24-checklist E3/E4)</summary>
    public string ContentSummary => ContentCounts is null || ContentCounts.Count == 0
        ? ""
        : string.Join("  ", ContentCounts.Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => $"{Abbrev(kv.Key)} {kv.Value}"));

    // Per-content-type counts for the mockup's dedicated grid columns (Sc/Lk/Cl/Hr/Pl/As/Mo/Po/Sk). (doc 26 · G-2.1)
    public int Scenes => Count("Scene");
    public int Looks => Count("Look");
    public int Clothing => Count("Clothing");
    public int Hair => Count("Hairstyle");
    public int Plugins => Count("Plugin");
    public int Assets => Count("Asset");
    public int Morphs => Count("Morph");
    public int Poses => Count("Pose");
    public int Skins => Count("Skin");
    private int Count(string type) => ContentCounts?.GetValueOrDefault(type) ?? 0;

    private static string Abbrev(string type) => type switch
    {
        "Scene" => "Sc", "Look" => "Lk", "Clothing" => "Cl", "Hairstyle" => "Hr",
        "Morph" => "Mo", "Plugin" => "Pl", _ => type.Length >= 2 ? type[..2] : type,
    };
}

/// <summary>A page of results plus the total match count (for the scrollbar / counts).</summary>
public sealed record LibraryPage(IReadOnlyList<PackageListEntry> Items, int TotalCount);

/// <summary>A creator and how many packages they own — feeds the searchable creator combo counts. (AC-10)</summary>
public sealed record CreatorCount(string Creator, int Count);

/// <summary>
/// Reads the materialized <c>PackageListItem</c> table for the library UI — filtered, sorted, and paged.
/// The public boundary the view-models bind to. (Checklist 1.38/1.41/1.46.)
/// </summary>
public interface ILibraryQueryService
{
    Task<LibraryPage> GetPageAsync(LibraryQuery query, CancellationToken cancellationToken = default);

    /// <summary>Distinct creators for the searchable creator combobox. (1.46)</summary>
    Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken cancellationToken = default);

    /// <summary>Distinct creators with owned-package counts, for the searchable creator combo. Default no-op so
    /// test doubles need not implement it; the real query service overrides it. (AC-10)</summary>
    Task<IReadOnlyList<CreatorCount>> GetCreatorCountsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CreatorCount>>([]);

    /// <summary>
    /// The full ordered package-id list for a (filter, sort) — the backbone of the O(1)-scroll
    /// OrderedSnapshot. Ignores <see cref="LibraryQuery.Skip"/>/<see cref="LibraryQuery.Take"/>. (1.39)
    /// </summary>
    Task<IReadOnlyList<long>> GetOrderedIdsAsync(LibraryQuery query, CancellationToken cancellationToken = default);

    /// <summary>Load DTOs for a bounded slice of an OrderedSnapshot, preserving the requested id order.</summary>
    Task<IReadOnlyList<PackageListEntry>> GetByIdsAsync(
        IReadOnlyList<long> packageIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PackageListEntry>>([]);
}
