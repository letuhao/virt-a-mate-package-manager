using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// The dirty-set drives derived-row recompute: after a base-table write (inserting a VarFile), marking
/// the owning package dirty and flushing refreshes exactly that package's materialized
/// <see cref="PackageListItem"/> row — no read-model row until the flush, and counts track the base
/// tables. (Checklist 0.26.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class DirtySetRefreshTests
{
    [Fact]
    public async Task Var_file_insert_then_flush_materializes_and_updates_the_read_model_row()
    {
        using var fx = new SqliteTestDatabase();
        var repoId = Guid.NewGuid();
        const long pkgId = 1;

        using (var db = fx.NewContext())
        {
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "r", MountPath = @"C:\r", IsOnline = true, IsEnabled = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            db.Packages.Add(new Package
            {
                Id = pkgId, VarName = "C.P.1", IdentityKey = "C.P.1", Creator = "C", PackageName = "P",
                VersionToken = "1", VersionSort = 1, FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
            });
            db.VarFiles.Add(NewVarFile(1, pkgId, repoId, "C.P.1.var"));
            await db.SaveChangesAsync();
        }

        // No read-model row exists yet — ApplyAsync/seed touched only base tables.
        using (var check = fx.NewContext())
            Assert.False(await check.PackageListItems.AnyAsync(x => x.PackageId == pkgId));

        // Base-table write already happened; the dirty-set schedules the derived-row refresh.
        var dirty = new ReadModelDirtySet();
        dirty.MarkPackage(pkgId);
        using (var writeDb = fx.NewContext())
            await dirty.FlushAsync(new EfCatalogStore(writeDb, new FakeClock()));

        using (var after = fx.NewContext())
        {
            var row = await after.PackageListItems.SingleAsync(x => x.PackageId == pkgId);
            Assert.Equal(1, row.OnlineInstanceCount);
            Assert.True(row.IsSingleCopy);
        }
        Assert.Equal(0, dirty.Count); // flush drained the set

        // A second copy is inserted (another base-table write); re-marking + flushing updates the same row.
        using (var writeDb = fx.NewContext())
        {
            writeDb.VarFiles.Add(NewVarFile(2, pkgId, repoId, "C.P.1.copy.var"));
            await writeDb.SaveChangesAsync();
        }
        dirty.MarkPackage(pkgId);
        using (var writeDb = fx.NewContext())
            await dirty.FlushAsync(new EfCatalogStore(writeDb, new FakeClock()));

        using (var after = fx.NewContext())
        {
            var row = await after.PackageListItems.SingleAsync(x => x.PackageId == pkgId);
            Assert.Equal(2, row.OnlineInstanceCount); // read model tracked the base-table change
            Assert.False(row.IsSingleCopy);
        }
    }

    private static VarFile NewVarFile(long id, long pkgId, Guid repoId, string path) => new()
    {
        Id = id, PackageId = pkgId, RepositoryId = repoId, RelativePath = path,
        SizeBytes = 1024, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
    };
}
