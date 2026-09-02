using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N11 · Alias CRUD over <see cref="VarAlias"/>. The key is folded the same way dependency refs are
/// (<see cref="IdentityFold"/>), so the resolver's alias map matches. After save/remove, rebuilds the
/// active preset's links so <c>___MissingVarLink___</c> symlinks appear immediately — legacy
/// <c>FormMissingVars.Createlink</c> behavior. (16-checklist BE-N11.)
/// </summary>
public sealed class EfAliasService(
    VarVaultDbContext db,
    IWriteQueue writeQueue,
    IDependencyResolver resolver,
    IPresetService presets,
    IActivationService activation,
    IProfileService profiles,
    ILogger<EfAliasService> logger) : IAliasService
{
    public async Task<Result> SetAsync(string missingRef, long ownedPackageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(missingRef))
            return Result.Failure("alias.ref", "Missing reference is required.");

        var result = await writeQueue.EnqueueScopedAsync(async (sp, ct) =>
        {
            var db = sp.GetRequiredService<VarVaultDbContext>();
            var resolver = sp.GetRequiredService<IDependencyResolver>();
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
        }, WritePriority.Interactive, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
            await MaterializeActiveLinksAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<IReadOnlyList<AliasDto>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.VarAliases
            .Select(a => new AliasDto(a.Id, a.MissingRefRaw, a.ResolvedVarName, a.ResolvedPackageId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Result> RemoveAsync(long aliasId, CancellationToken cancellationToken = default)
    {
        var result = await writeQueue.EnqueueScopedAsync(async (sp, ct) =>
        {
            var db = sp.GetRequiredService<VarVaultDbContext>();
            var resolver = sp.GetRequiredService<IDependencyResolver>();
            var alias = await db.VarAliases.FirstOrDefaultAsync(a => a.Id == aliasId, ct).ConfigureAwait(false);
            if (alias is null)
                return Result.Failure("alias.missing", "Alias not found.");
            var missingRef = alias.MissingRefRaw;
            db.VarAliases.Remove(alias);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await resolver.ResolveReferenceAsync(missingRef, ct).ConfigureAwait(false);
            return Result.Success();
        }, WritePriority.Interactive, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
            await MaterializeActiveLinksAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Outside the write-queue action (avoids re-entrancy deadlock): rebuild active profile so alias
    /// symlinks under <c>___MissingVarLink___</c> match the DB — same effect as legacy Createlink on OK.
    /// </summary>
    private async Task MaterializeActiveLinksAsync(CancellationToken cancellationToken)
    {
        try
        {
            var helper = new ActivePresetActivationHelper(db, presets, activation, profiles);
            var rebuild = await helper.RebuildActiveAsync(cancellationToken).ConfigureAwait(false);
            if (rebuild.PrivilegeFailures > 0)
            {
                logger.LogWarning(
                    "Alias saved but ___MissingVarLink___ symlink needs Developer Mode / admin privilege.");
            }
            else if (rebuild.PathUnavailable > 0)
            {
                logger.LogWarning(
                    "Alias saved but VaM path is unset/unavailable — set Settings, then Activate or re-link.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Alias DB update succeeded but active-profile link rebuild failed.");
        }
    }
}
