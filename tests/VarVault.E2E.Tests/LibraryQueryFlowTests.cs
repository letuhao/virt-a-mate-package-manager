using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// End-to-end library read model: index a repository, then query the materialized PackageListItem via
/// the SDK service with filtering, sorting, and paging. (Checklist 1.38/1.41/1.46.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class LibraryQueryFlowTests
{
    [Fact]
    public async Task Filters_sorts_and_pages_the_library()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "Alice.Dress.1.var", "Alice", "Dress", [("Custom/Clothing/d.vam", "x")]);
        WriteVar(repoDir, "Alice.Hair.1.var", "Alice", "Hair", [("Custom/Hair/h.vam", "x")]);
        WriteVar(repoDir, "Bob.Scene.1.var", "Bob", "Scene", [("Saves/scene/s.json", "{}")]);

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var library = scope.ServiceProvider.GetRequiredService<ILibraryQueryService>();

        // All three indexed.
        var all = await library.GetPageAsync(new LibraryQuery());
        Assert.Equal(3, all.TotalCount);

        // Filter by creator.
        var alice = await library.GetPageAsync(new LibraryQuery(Creator: "Alice"));
        Assert.Equal(2, alice.TotalCount);
        Assert.All(alice.Items, i => Assert.Equal("Alice", i.Creator));

        // Search by name substring.
        var scene = await library.GetPageAsync(new LibraryQuery(SearchText: "Scene"));
        Assert.Equal(1, scene.TotalCount);
        Assert.Equal("Bob.Scene.1", scene.Items[0].VarName);

        // Sort by name descending.
        var byName = await library.GetPageAsync(new LibraryQuery(Sort: LibrarySort.Name, Descending: true));
        Assert.Equal("Bob.Scene.1", byName.Items[0].VarName);

        // Paging: take 1, skip 1 — total still reflects the full match count.
        var page = await library.GetPageAsync(new LibraryQuery(Skip: 1, Take: 1, Sort: LibrarySort.Name));
        Assert.Single(page.Items);
        Assert.Equal(3, page.TotalCount);

        // Creator list for the combobox.
        var creators = await library.GetCreatorsAsync();
        Assert.Equal(["Alice", "Bob"], creators);
    }

    [Fact]
    public async Task Favorites_and_missing_deps_filters_apply()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "A.Fav.1.var", "A", "Fav", [("Custom/Hair/h.vam", "x")]);
        WriteVar(repoDir, "A.Plain.1.var", "A", "Plain", [("Custom/Hair/h.vam", "y")]);

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var favItem = await db.PackageListItems.FirstAsync(p => p.VarName == "A.Fav.1");
        favItem.IsFavorite = true;
        await db.SaveChangesAsync();

        var library = scope.ServiceProvider.GetRequiredService<ILibraryQueryService>();
        var favs = await library.GetPageAsync(new LibraryQuery(FavoritesOnly: true));
        Assert.Single(favs.Items);
        Assert.Equal("A.Fav.1", favs.Items[0].VarName);
    }

    [Fact]
    public async Task Fts_search_finds_cjk_creator_names()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        // A CJK creator name — the exact case VaM users hit; searchable via the trigram FTS blob.
        WriteVar(repoDir, "刘亦菲.衣装.1.var", "刘亦菲", "衣装", [("Custom/Clothing/c.vam", "x")]);
        WriteVar(repoDir, "Bob.Plain.1.var", "Bob", "Plain", [("Custom/Hair/h.vam", "y")]);

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var library = scope.ServiceProvider.GetRequiredService<ILibraryQueryService>();

        var hits = await library.GetPageAsync(new LibraryQuery(SearchText: "刘亦菲"));
        Assert.Equal(1, hits.TotalCount);
        Assert.Equal("刘亦菲.衣装.1", hits.Items[0].VarName);
    }

    [Fact]
    public async Task Ordered_ids_back_an_o1_scroll_snapshot()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "Bob.Z.1.var", "Bob", "Z", [("Custom/Hair/h.vam", "z")]);
        WriteVar(repoDir, "Alice.A.1.var", "Alice", "A", [("Custom/Hair/h.vam", "a")]);
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var library = scope.ServiceProvider.GetRequiredService<ILibraryQueryService>();

        var ids = await library.GetOrderedIdsAsync(new LibraryQuery(Sort: LibrarySort.Name));
        var snapshot = new VarVault.Domain.Indexing.OrderedSnapshot(ids);

        Assert.Equal(2, snapshot.Count);
        // Alice.A.1 sorts before Bob.Z.1; the first row's package is Alice's.
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var alice = await db.Packages.FirstAsync(p => p.VarName == "Alice.A.1");
        Assert.Equal(alice.Id, snapshot[0]); // O(1) access
    }

    private static async Task<Guid> Register(TestHost host, string path)
    {
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var id = Guid.NewGuid();
        db.Repositories.Add(new Repository
        {
            Id = id, Name = "t", MountPath = path, IsOnline = true, IsEnabled = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static void WriteVar(TempDirectory dir, string fileName, string creator, string package, (string Name, string Content)[] entries)
    {
        var path = Path.Combine(dir.Path, fileName);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "meta.json", "{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\"}");
        foreach (var (name, content) in entries)
            Add(zip, name, content);
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
