using VarVault.Common;

namespace VarVault.Sdk.Library;

/// <summary>A tag with how many packages carry it.</summary>
public sealed record TagInfo(long Id, string Name, int PackageCount);

/// <summary>
/// BE-N12 · User tags over Tag/PackageTag. Create tags, tag/untag packages, list with counts, and list a
/// tag's package ids (for the library rail). (16-checklist BE-N12.)
/// </summary>
public interface ITagService
{
    Task<Result<TagInfo>> CreateAsync(string name, CancellationToken cancellationToken = default);
    Task<Result> TagAsync(long packageId, long tagId, CancellationToken cancellationToken = default);
    Task<Result> UntagAsync(long packageId, long tagId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TagInfo>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<long>> PackageIdsAsync(long tagId, CancellationToken cancellationToken = default);
}
