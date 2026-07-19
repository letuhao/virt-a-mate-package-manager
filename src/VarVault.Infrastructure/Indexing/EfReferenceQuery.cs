using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Dependencies;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// EF reference query spanning all reference sources for safe-delete orphan reasoning. (Checklist 2.12.)
/// </summary>
public sealed class EfReferenceQuery(VarVaultDbContext db) : IReferenceQuery
{
    public async Task<bool> IsReferencedAsync(long packageId, CancellationToken cancellationToken = default)
    {
        if (await db.Dependencies.AnyAsync(d => d.ResolvedPackageId == packageId, cancellationToken).ConfigureAwait(false))
            return true;
        if (await db.SaveDependencies.AnyAsync(d => d.ResolvedPackageId == packageId, cancellationToken).ConfigureAwait(false))
            return true;
        if (await db.PresetMembers.AnyAsync(m => m.ResolvedPackageId == packageId, cancellationToken).ConfigureAwait(false))
            return true;
        if (await db.VarAliases.AnyAsync(a => a.ResolvedPackageId == packageId, cancellationToken).ConfigureAwait(false))
            return true;

        // ActivationLink → VarFile → Package
        return await db.ActivationLinks
            .AnyAsync(l => db.VarFiles.Any(v => v.Id == l.VarFileId && v.PackageId == packageId), cancellationToken)
            .ConfigureAwait(false);
    }
}
