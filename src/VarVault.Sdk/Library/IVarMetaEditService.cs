using VarVault.Common;

namespace VarVault.Sdk.Library;

/// <summary>Editable snapshot of a package's <c>meta.json</c> for the Edit meta dialog.</summary>
public sealed record VarMetaEditDraft(
    long PackageId,
    long VarFileId,
    string VarName,
    string AbsolutePath,
    string? CreatorName,
    string? PackageName,
    string? LicenseType,
    string? Description,
    string? ProgramVersion,
    IReadOnlyList<string> DependencyRefs,
    IReadOnlyList<string> Warnings);

/// <summary>User-edited fields to write back into <c>meta.json</c>.</summary>
public sealed record VarMetaEditRequest(
    long PackageId,
    long? VarFileId,
    string? CreatorName,
    string? PackageName,
    string? LicenseType,
    string? Description,
    string? ProgramVersion,
    IReadOnlyList<string> DependencyRefs);

/// <summary>Outcome of a successful meta rewrite + catalog refresh.</summary>
public sealed record VarMetaEditResult(
    long PackageId,
    long VarFileId,
    string AbsolutePath,
    string? TrashId,
    int DependencyCount);

/// <summary>
/// Load and rewrite a package's on-disk <c>meta.json</c> (dependencies + common fields), then refresh
/// the catalog. Replaces the online copy in place; the previous file is moved to Trash.
/// </summary>
public interface IVarMetaEditService
{
    Task<Result<VarMetaEditDraft>> LoadAsync(long packageId, long? varFileId = null, CancellationToken cancellationToken = default);

    Task<Result<VarMetaEditResult>> SaveAsync(VarMetaEditRequest request, CancellationToken cancellationToken = default);
}
