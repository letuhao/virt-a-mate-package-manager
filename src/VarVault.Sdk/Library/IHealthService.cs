using VarVault.Common;
using VarVault.Sdk.Paging;

namespace VarVault.Sdk.Library;

/// <summary>Encoding-fix group: how many vars share a detected legacy codepage.</summary>
public sealed record EncodingGroup(string Codepage, int Count);

/// <summary>A var flagged for integrity/corruption or missing meta.</summary>
public sealed record IntegrityIssue(long VarFileId, string VarName, string RelativePath);

/// <summary>
/// BE-N5 · Health facade over EncodingHealthEngine + EncodingFixCoordinator. Surfaces encoding-fix groups
/// (by codepage) and integrity issues, and fixes a broken var into a new UTF-8 var (original retained).
/// (16-checklist BE-N5.)
/// </summary>
public interface IHealthService
{
    Task<IReadOnlyList<EncodingGroup>> EncodingGroupsAsync(CancellationToken cancellationToken = default);
    async Task<PageResult<IntegrityIssue>> IntegrityPageAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        var all = await IntegrityAsync(cancellationToken).ConfigureAwait(false);
        var page = request.Normalize();
        return new PageResult<IntegrityIssue>(all.Skip(page.Skip).Take(page.SafePageSize).ToList(), all.Count, page.SafePageNumber, page.SafePageSize);
    }
    Task<IReadOnlyList<IntegrityIssue>> IntegrityAsync(CancellationToken cancellationToken = default);

    /// <summary>Vars missing a parseable <c>meta.json</c> (IntegrityStatus.MissingMeta). (24-checklist A4.)</summary>
    async Task<PageResult<IntegrityIssue>> MissingMetaPageAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        var all = await MissingMetaAsync(cancellationToken).ConfigureAwait(false);
        var page = request.Normalize();
        return new PageResult<IntegrityIssue>(all.Skip(page.Skip).Take(page.SafePageSize).ToList(), all.Count, page.SafePageNumber, page.SafePageSize);
    }
    Task<IReadOnlyList<IntegrityIssue>> MissingMetaAsync(CancellationToken cancellationToken = default);

    /// <summary>Fix one broken var → a new UTF-8 var; returns the new VarFile id.</summary>
    Task<Result<long>> FixAsync(long varFileId, CancellationToken cancellationToken = default);
}
