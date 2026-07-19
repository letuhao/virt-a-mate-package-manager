using VarVault.Common;

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
    Task<IReadOnlyList<IntegrityIssue>> IntegrityAsync(CancellationToken cancellationToken = default);

    /// <summary>Fix one broken var → a new UTF-8 var; returns the new VarFile id.</summary>
    Task<Result<long>> FixAsync(long varFileId, CancellationToken cancellationToken = default);
}
