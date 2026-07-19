using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Library;
using VarVault.Infrastructure.Persistence;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>BE-N9 · Package detail: identity, copies, content items, forward closure, reverse count. (16-checklist BE-N9.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class EfPackageDetailQueryTests
{
    [Fact]
    public async Task Returns_copies_content_items_and_forward_closure()
    {
        using var fx = new SqliteTestDatabase();
        var repoId = Guid.NewGuid();
        using (var db = fx.NewContext())
        {
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "r", MountPath = @"C:\r", Tier = 1, MediaType = MediaType.Nvme,
                IsOnline = true, IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            Pkg(db, 1, "A.P.1", license: "CC BY");
            Pkg(db, 2, "B.P.1", license: null);
            db.VarFiles.Add(Var(10, 1, repoId, "a.var"));
            db.VarFiles.Add(Var(11, 1, repoId, "a.copy.var"));
            db.ContentItems.Add(new ContentItem { Id = 1, VarFileId = 10, Type = ContentType.Scene, EntryPath = "Saves/s.json" });
            db.ContentItems.Add(new ContentItem { Id = 2, VarFileId = 10, Type = ContentType.Look, EntryPath = "Custom/look.vap", IsPreset = true });
            db.Dependencies.Add(new Dependency { Id = 1, VarFileId = 10, ResolvedPackageId = 2, DependsOnRefKey = "B.P.1", DependsOnRefRaw = "B.P.1" });
            db.PackageListItems.Add(new PackageListItem
            {
                PackageId = 1, VarName = "A.P.1", Creator = "A", PackageName = "P", VersionToken = "1",
                PrimaryType = ContentType.Scene, TotalSize = 5000, Class = ContentClass.Hot, AddedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            var a = await db.Packages.FindAsync(1L);
            a!.CanonicalVarFileId = 10;
            a.ReverseDependentCount = 5;
            await db.SaveChangesAsync();
        }

        using var read = fx.NewContext();
        var detail = await new EfPackageDetailQuery(read, new EfDependencyGraph(read)).GetAsync(1);

        Assert.NotNull(detail);
        Assert.Equal("A.P.1", detail!.VarName);
        Assert.Equal("CC BY", detail.License);
        Assert.Equal(5000, detail.TotalSize);
        Assert.Equal("Hot", detail.StorageClass);
        Assert.Equal(5, detail.DependedOnByCount);
        Assert.Equal(2, detail.Copies.Count);
        Assert.Equal(2, detail.ContentItems.Count);
        Assert.Contains(2L, detail.ForwardClosure); // A depends on B
    }

    [Fact]
    public async Task Missing_package_returns_null()
    {
        using var fx = new SqliteTestDatabase();
        using var read = fx.NewContext();
        Assert.Null(await new EfPackageDetailQuery(read, new EfDependencyGraph(read)).GetAsync(999));
    }

    private static void Pkg(VarVaultDbContext db, long id, string name, string? license) =>
        db.Packages.Add(new Package
        {
            Id = id, VarName = name, IdentityKey = name.ToUpperInvariant(), Creator = name.Split('.')[0],
            PackageName = name.Split('.')[1], VersionToken = "1", VersionSort = 1, LicenseType = license,
            FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        });

    private static VarFile Var(long id, long pkgId, Guid repoId, string rel) => new()
    {
        Id = id, PackageId = pkgId, RepositoryId = repoId, RelativePath = rel, SizeBytes = 2500,
        FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
    };
}
