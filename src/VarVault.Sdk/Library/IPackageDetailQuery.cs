using VarVault.Sdk.Paging;

namespace VarVault.Sdk.Library;

/// <summary>A content entry inside a var (for the previews/content-items tab).</summary>
public sealed record ContentItemDto(string Type, string EntryPath, bool IsPreset);

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
    int DependedOnByCount);

/// <summary>Full detail for one package — the var-detail modal and the library detail panel bind this.</summary>
public sealed record PackageDetail(
    long PackageId,
    string VarName,
    string IdentityKey,
    string? License,
    long TotalSize,
    string StorageClass,
    int DependedOnByCount,
    IReadOnlyList<long> ForwardClosure,
    IReadOnlyList<ContentItemDto> ContentItems,
    IReadOnlyList<CopyDto> Copies);

/// <summary>
/// BE-N9 · Per-package detail: identity, license, class, reverse-dependent count, forward dependency
/// closure, content items, and copies/lineage. (16-checklist BE-N9.)
/// </summary>
public interface IPackageDetailQuery
{
    async Task<PackageDetailOverview?> GetOverviewAsync(long packageId, CancellationToken cancellationToken = default)
    {
        var full = await GetAsync(packageId, cancellationToken).ConfigureAwait(false);
        return full is null ? null : new PackageDetailOverview(
            full.PackageId, full.VarName, full.IdentityKey, full.License, full.TotalSize, full.StorageClass, full.DependedOnByCount);
    }

    async Task<PageResult<long>> GetForwardClosurePageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default)
    {
        var full = await GetAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (full is null)
            return PageResult<long>.Empty(request);
        var page = request.Normalize();
        return new PageResult<long>(full.ForwardClosure.Skip(page.Skip).Take(page.SafePageSize).ToList(), full.ForwardClosure.Count, page.SafePageNumber, page.SafePageSize);
    }

    async Task<PageResult<ContentItemDto>> GetContentItemsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default)
    {
        var full = await GetAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (full is null)
            return PageResult<ContentItemDto>.Empty(request);
        var page = request.Normalize();
        return new PageResult<ContentItemDto>(full.ContentItems.Skip(page.Skip).Take(page.SafePageSize).ToList(), full.ContentItems.Count, page.SafePageNumber, page.SafePageSize);
    }

    async Task<PageResult<CopyDto>> GetCopiesPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default)
    {
        var full = await GetAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (full is null)
            return PageResult<CopyDto>.Empty(request);
        var page = request.Normalize();
        return new PageResult<CopyDto>(full.Copies.Skip(page.Skip).Take(page.SafePageSize).ToList(), full.Copies.Count, page.SafePageNumber, page.SafePageSize);
    }

    Task<PackageDetail?> GetAsync(long packageId, CancellationToken cancellationToken = default);
}
