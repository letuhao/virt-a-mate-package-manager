namespace VarVault.Sdk.Library;

/// <summary>A content entry inside a var (for the previews/content-items tab).</summary>
public sealed record ContentItemDto(string Type, string EntryPath, bool IsPreset);

/// <summary>A physical copy of a package (for the copies & lineage tab).</summary>
public sealed record CopyDto(long VarFileId, int Tier, string Path, long SizeBytes, bool IsOnline, long? FixedFromVarFileId);

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
    Task<PackageDetail?> GetAsync(long packageId, CancellationToken cancellationToken = default);
}
