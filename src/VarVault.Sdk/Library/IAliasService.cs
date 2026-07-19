using VarVault.Common;

namespace VarVault.Sdk.Library;

/// <summary>A saved missing-dependency alias.</summary>
public sealed record AliasDto(long Id, string MissingRef, string? ResolvedVarName, long? ResolvedPackageId);

/// <summary>
/// BE-N11 · Missing-dependency aliases: map a missing reference to an owned package. The dependency
/// resolver consults these, so a saved alias makes the reference resolve on every run. (16-checklist BE-N11.)
/// </summary>
public interface IAliasService
{
    Task<Result> SetAsync(string missingRef, long ownedPackageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AliasDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result> RemoveAsync(long aliasId, CancellationToken cancellationToken = default);
}
