using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Dependencies;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Forward dependency closure via a recursive CTE over resolved edges of each package's canonical var.
/// <c>UNION</c> (set semantics) makes it cycle-safe; a depth column caps pathological chains.
/// (Checklist 2.10.)
/// </summary>
public sealed class EfDependencyGraph(VarVaultDbContext db) : IDependencyGraph
{
    public async Task<IReadOnlyList<long>> ForwardClosureAsync(long packageId, int maxDepth = 50, CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH RECURSIVE closure(id, depth) AS (
                SELECT {0}, 0
                UNION
                SELECT d."ResolvedPackageId", closure.depth + 1
                FROM closure
                JOIN "Package" p ON p."Id" = closure.id
                JOIN "Dependency" d ON d."VarFileId" = p."CanonicalVarFileId"
                WHERE d."ResolvedPackageId" IS NOT NULL AND closure.depth < {1}
            )
            SELECT DISTINCT id AS "Value" FROM closure WHERE id <> {0}
            """;

        return await db.Database
            .SqlQueryRaw<long>(sql, packageId, maxDepth)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
