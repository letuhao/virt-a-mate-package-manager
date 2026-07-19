using VarVault.Domain.Entities;
using VarVault.Infrastructure.Library;
using VarVault.Infrastructure.Persistence;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>BE-N14 · Command palette combines nav/action registry hits with package search. (16-checklist BE-N14.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class EfCommandPaletteServiceTests
{
    [Fact]
    public async Task Search_returns_nav_action_and_package_hits()
    {
        using var fx = new SqliteTestDatabase();
        using (var db = fx.NewContext())
        {
            db.Packages.Add(new Package
            {
                Id = 1, VarName = "Foo.Bar.1", IdentityKey = "FOO.BAR.1", Creator = "Foo", PackageName = "Bar",
                VersionToken = "1", VersionSort = 1, FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
            });
            db.PackageListItems.Add(new PackageListItem
            {
                PackageId = 1, VarName = "Foo.Bar.1", Creator = "Foo", PackageName = "Bar", VersionToken = "1",
                PrimaryType = ContentType.Scene, Class = ContentClass.Cold, AddedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        using var read = fx.NewContext();
        var svc = new EfCommandPaletteService(new EfLibraryQueryService(read));

        var nav = await svc.SearchAsync("Settings");
        Assert.Contains(nav, h => h.Kind == "nav" && h.Target == "settings");

        var action = await svc.SearchAsync("index");
        Assert.Contains(action, h => h.Kind == "action" && h.Target == "index");

        var pkg = await svc.SearchAsync("Ba"); // short → LIKE over the read model
        Assert.Contains(pkg, h => h.Kind == "package" && h.Title == "Foo.Bar.1");

        Assert.Empty(await svc.SearchAsync(""));
    }
}
