using VarVault.Domain.Entities;
using VarVault.Infrastructure.Library;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>BE-N12 · Library facet filters (packageName/installed/single/types/tiers) + tag CRUD. (16-checklist BE-N12.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class LibraryFacetsTests
{
    [Fact]
    public async Task Facet_filters_narrow_the_page()
    {
        using var fx = new SqliteTestDatabase();
        using (var db = fx.NewContext())
        {
            Seed(db, 1, "Alpha", active: true, ContentType.Scene, tier: 1, single: true);
            Seed(db, 2, "Beta", active: false, ContentType.Look, tier: 3, single: false);
            Seed(db, 3, "Alphabet", active: true, ContentType.Scene, tier: 1, single: false);
            await db.SaveChangesAsync();
        }
        using var read = fx.NewContext();
        var svc = new EfLibraryQueryService(read);

        Assert.Equal(2, (await svc.GetPageAsync(new LibraryQuery(PackageName: "Alpha"))).TotalCount);   // Alpha, Alphabet
        Assert.Equal(2, (await svc.GetPageAsync(new LibraryQuery(InstalledOnly: true))).TotalCount);     // active only
        Assert.Equal(1, (await svc.GetPageAsync(new LibraryQuery(SingleCopyOnly: true))).TotalCount);    // P1
        Assert.Equal(1, (await svc.GetPageAsync(new LibraryQuery(Types: ["Look"]))).TotalCount);         // P2
        Assert.Equal(1, (await svc.GetPageAsync(new LibraryQuery(Tiers: [3]))).TotalCount);              // P2
    }

    [Fact]
    public async Task Tag_create_apply_list_and_query()
    {
        using var fx = new SqliteTestDatabase();
        using (var db = fx.NewContext())
        {
            Seed(db, 1, "A", active: true, ContentType.Scene, tier: 1, single: true);
            await db.SaveChangesAsync();
        }
        using var db2 = fx.NewContext();
        var tags = new EfTagService(db2);

        var tag = await tags.CreateAsync("keep-forever");
        Assert.True(tag.IsSuccess);
        Assert.True((await tags.TagAsync(1, tag.Value.Id)).IsSuccess);

        var list = await tags.ListAsync();
        Assert.Contains(list, t => t.Name == "keep-forever" && t.PackageCount == 1);
        Assert.Equal([1L], await tags.PackageIdsAsync(tag.Value.Id));

        Assert.True((await tags.UntagAsync(1, tag.Value.Id)).IsSuccess);
        Assert.Empty(await tags.PackageIdsAsync(tag.Value.Id));
    }

    private static void Seed(VarVaultDbContext db, long id, string pkgName, bool active, ContentType type, int tier, bool single)
    {
        var name = $"C.{pkgName}.1";
        db.Packages.Add(new Package
        {
            Id = id, VarName = name, IdentityKey = name.ToUpperInvariant(), Creator = "C", PackageName = pkgName,
            VersionToken = "1", VersionSort = 1, FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        });
        db.PackageListItems.Add(new PackageListItem
        {
            PackageId = id, VarName = name, Creator = "C", PackageName = pkgName, VersionToken = "1",
            PrimaryType = type, TotalSize = 1000, Class = ContentClass.Cold, IsActive = active,
            IsSingleCopy = single, ActualTierMin = tier, AddedAt = DateTime.UtcNow,
        });
    }
}
