using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Domain.Indexing;
using VarVault.Domain.ValueObjects;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Catalog persist must survive re-index of dense dependency lists and lineage-linked deletes —
/// both used to abort the indexer job (UNIQUE Dependency / FK on RemoveVarFiles).
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class CatalogPersistResilienceTests
{
    [Fact]
    public async Task Reapply_with_overlapping_deps_does_not_hit_unique_constraint()
    {
        using var fx = new SqliteTestDatabase();
        var repoId = Guid.NewGuid();
        using (var db = fx.NewContext())
        {
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "r", MountPath = @"C:\r", IsOnline = true, IsEnabled = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var identity = PackageId.TryParse("Dense.Deps.1");
        Assert.True(identity.IsSuccess);
        var refs = Enumerable.Range(1, 40).Select(i => $"Creator.Pack{i}.1").ToList();
        // Overlap meta + embedded with same fold keys — AddRefs must dedupe.
        var upsert = new VarUpsert(
            repoId, "Dense.Deps.1.var", 100, DateTime.UtcNow, QuarantineKind.None,
            identity.Value, IntegrityStatus.Ok,
            null, null, null, null, null,
            "sig", null, null,
            EncodingHealth.Ok, null, 0,
            [], new Dictionary<ContentType, int>(),
            refs, refs);

        using (var db = fx.NewContext())
        {
            var store = new EfCatalogStore(db, new FakeClock());
            Assert.NotNull(await store.ApplyAsync(upsert));
            Assert.NotNull(await store.ApplyAsync(upsert)); // re-index same file
        }

        using (var check = fx.NewContext())
        {
            var vf = await check.VarFiles.SingleAsync();
            var deps = await check.Dependencies.Where(d => d.VarFileId == vf.Id).ToListAsync();
            Assert.Equal(40, deps.Count);
            Assert.Equal(deps.Count, deps.Select(d => d.DependsOnRefKey).Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public async Task RemoveVarFiles_clears_lineage_refs_without_fk_failure()
    {
        using var fx = new SqliteTestDatabase();
        var repoId = Guid.NewGuid();
        using (var db = fx.NewContext())
        {
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "r", MountPath = @"C:\r", IsOnline = true, IsEnabled = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            db.Packages.Add(new Package
            {
                Id = 1, VarName = "A.B.1", IdentityKey = "a.b.1",
                Creator = "A", PackageName = "B", VersionToken = "1", VersionSort = 1,
                FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
            });
            db.VarFiles.Add(new VarFile
            {
                Id = 10, PackageId = 1, RepositoryId = repoId, RelativePath = "broken.var",
                SizeBytes = 1, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
            });
            db.VarFiles.Add(new VarFile
            {
                Id = 11, PackageId = 1, RepositoryId = repoId, RelativePath = "fixed.var",
                SizeBytes = 1, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
                FixedFromVarFileId = 10,
            });
            await db.SaveChangesAsync();
            var broken = await db.VarFiles.SingleAsync(v => v.Id == 10);
            broken.SupersededByVarFileId = 11;
            var pkg = await db.Packages.SingleAsync(p => p.Id == 1);
            pkg.CanonicalVarFileId = 10;
            await db.SaveChangesAsync();
        }

        using (var db = fx.NewContext())
        {
            var store = new EfCatalogStore(db, new FakeClock());
            var removed = await store.RemoveVarFilesAsync([10]);
            Assert.Equal(1, removed);
        }

        using (var check = fx.NewContext())
        {
            Assert.False(await check.VarFiles.AnyAsync(v => v.Id == 10));
            var fixedVar = await check.VarFiles.SingleAsync(v => v.Id == 11);
            Assert.Null(fixedVar.FixedFromVarFileId);
            var pkg = await check.Packages.SingleAsync(p => p.Id == 1);
            Assert.Equal(11, pkg.CanonicalVarFileId);
        }
    }
}
