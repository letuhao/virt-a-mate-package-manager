using VarVault.Sdk.Paging;

namespace VarVault.Sdk.Library;

/// <summary>Resolution state for a dependency edge shown in detail UI.</summary>
public enum DependencyResolutionState
{
    Resolved = 0,
    Missing = 1,
    Substituted = 2,
    Alias = 3,
}

/// <summary>Compact resolved package card for dependency navigation.</summary>
public sealed record ResolvedPackageCard(
    long PackageId,
    string VarName,
    string PrimaryType,
    string StorageClass);

/// <summary>A direct dependency edge with raw ref, resolution state, and optional resolved card.</summary>
public sealed record DependencyEdgeDto(
    string RequestedRefRaw,
    string RequestedRefKey,
    DependencyResolutionState State,
    bool IsVersionSubstituted,
    ResolvedPackageCard? ResolvedPackage);

/// <summary>A package that depends on the current one (reverse closure entry).</summary>
public sealed record ReverseDependentDto(
    long PackageId,
    string VarName,
    string PrimaryType);

/// <summary>A content entry inside a var (for the previews/content-items tab).</summary>
public sealed record ContentItemDto(
    long ContentItemId,
    string Type,
    string EntryPath,
    bool IsPreset,
    bool HasPreview,
    /// <summary>True when a wall thumbnail is already packed in the thumbnail store.</summary>
    bool PreviewCached = false);

/// <summary>A physical copy of a package (for the copies & lineage tab).</summary>
public sealed record CopyDto(long VarFileId, int Tier, string Path, long SizeBytes, bool IsOnline, long? FixedFromVarFileId);

/// <summary>Overview/header portion of package detail, without potentially large child lists.</summary>
public sealed record PackageDetailOverview(
    long PackageId,
    string VarName,
    string IdentityKey,
    string? License,
    long TotalSize,
    string StorageClass,
    int DependedOnByCount,
    bool IsPinnedHot = false,
    bool IsForcedCold = false);

/// <summary>Full detail for one package — the var-detail modal and the library detail panel bind this.</summary>
public sealed record PackageDetail(
    long PackageId,
    string VarName,
    string IdentityKey,
    string? License,
    long TotalSize,
    string StorageClass,
    int DependedOnByCount,
    IReadOnlyList<CopyDto> Copies,
    bool IsPinnedHot = false,
    bool IsForcedCold = false);

/// <summary>
/// BE-N9 · Per-package detail: identity, license, class, reverse-dependent count, dependency edges,
/// content items, and copies/lineage. (16-checklist BE-N9.)
/// </summary>
public interface IPackageDetailQuery
{
    Task<PackageDetailOverview?> GetOverviewAsync(long packageId, CancellationToken cancellationToken = default);

    Task<PageResult<DependencyEdgeDto>> GetDirectDependenciesPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default);

    Task<PageResult<ReverseDependentDto>> GetReverseDependentsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default);

    Task<PageResult<DependencyEdgeDto>> GetSaveDependentsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default);

    Task<PageResult<ContentItemDto>> GetContentItemsPageAsync(
        long packageId,
        long? varFileId,
        PageRequest request,
        CancellationToken cancellationToken = default,
        string? typeFilter = null,
        bool loadableOnly = false);

    Task<PageResult<CopyDto>> GetCopiesPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default);

    Task<PackageDetail?> GetAsync(long packageId, CancellationToken cancellationToken = default);
}
