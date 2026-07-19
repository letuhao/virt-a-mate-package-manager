using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N11 · Alias CRUD over <see cref="VarAlias"/>. The key is folded the same way dependency refs are
/// (<see cref="IdentityFold"/>), so the resolver's alias map matches. (16-checklist BE-N11.)
/// </summary>
public sealed class EfAliasService(VarVaultDbContext db) : IAliasService
{
    public async Task<Result> SetAsync(string missingRef, long ownedPackageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(missingRef))
            return Result.Failure("alias.ref", "Missing reference is required.");
        var owned = await db.Packages.FirstOrDefaultAsync(p => p.Id == ownedPackageId, cancellationToken).ConfigureAwait(false);
        if (owned is null)
            return Result.Failure("alias.target", "Owned package not found.");

        var key = IdentityFold.Compute(missingRef);
        var alias = await db.VarAliases
            .FirstOrDefaultAsync(a => a.MissingRefKey == key && a.Scope == AliasScope.Global, cancellationToken)
            .ConfigureAwait(false);
        if (alias is null)
        {
            alias = new VarAlias { MissingRefKey = key, MissingRefRaw = missingRef, Scope = AliasScope.Global };
            db.VarAliases.Add(alias);
        }
        alias.ResolvedPackageId = ownedPackageId;
        alias.ResolvedVarName = owned.VarName;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<IReadOnlyList<AliasDto>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.VarAliases
            .Select(a => new AliasDto(a.Id, a.MissingRefRaw, a.ResolvedVarName, a.ResolvedPackageId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Result> RemoveAsync(long aliasId, CancellationToken cancellationToken = default)
    {
        var alias = await db.VarAliases.FirstOrDefaultAsync(a => a.Id == aliasId, cancellationToken).ConfigureAwait(false);
        if (alias is null)
            return Result.Failure("alias.missing", "Alias not found.");
        db.VarAliases.Remove(alias);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
