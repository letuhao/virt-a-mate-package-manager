using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N11 · Alias CRUD over <see cref="VarAlias"/>. The key is folded the same way dependency refs are
/// (<see cref="IdentityFold"/>), so the resolver's alias map matches. (16-checklist BE-N11.)
/// </summary>
public sealed class EfAliasService(
    VarVaultDbContext db,
    IWriteQueue writeQueue,
    IDependencyResolver resolver) : IAliasService
{
    public Task<Result> SetAsync(string missingRef, long ownedPackageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(missingRef))
            return Task.FromResult(Result.Failure("alias.ref", "Missing reference is required."));

        return writeQueue.EnqueueAsync(async ct =>
        {
            var owned = await db.Packages.FirstOrDefaultAsync(p => p.Id == ownedPackageId, ct).ConfigureAwait(false);
            if (owned is null)
                return Result.Failure("alias.target", "Owned package not found.");

            var key = IdentityFold.Compute(missingRef);
            var alias = await db.VarAliases
                .FirstOrDefaultAsync(a => a.MissingRefKey == key && a.Scope == AliasScope.Global, ct)
                .ConfigureAwait(false);
            if (alias is null)
            {
                alias = new VarAlias { MissingRefKey = key, MissingRefRaw = missingRef, Scope = AliasScope.Global };
                db.VarAliases.Add(alias);
            }
            alias.ResolvedPackageId = ownedPackageId;
            alias.ResolvedVarName = owned.VarName;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await resolver.ResolveReferenceAsync(missingRef, ct).ConfigureAwait(false);
            return Result.Success();
        }, WritePriority.Interactive, cancellationToken);
    }

    public async Task<IReadOnlyList<AliasDto>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.VarAliases
            .Select(a => new AliasDto(a.Id, a.MissingRefRaw, a.ResolvedVarName, a.ResolvedPackageId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<Result> RemoveAsync(long aliasId, CancellationToken cancellationToken = default) =>
        writeQueue.EnqueueAsync(async ct =>
        {
            var alias = await db.VarAliases.FirstOrDefaultAsync(a => a.Id == aliasId, ct).ConfigureAwait(false);
            if (alias is null)
                return Result.Failure("alias.missing", "Alias not found.");
            var missingRef = alias.MissingRefRaw;
            db.VarAliases.Remove(alias);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await resolver.ResolveReferenceAsync(missingRef, ct).ConfigureAwait(false);
            return Result.Success();
        }, WritePriority.Interactive, cancellationToken);
}
