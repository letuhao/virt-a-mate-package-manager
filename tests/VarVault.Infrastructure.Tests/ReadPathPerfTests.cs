using System.Diagnostics;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Library;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Read-path performance at scale: with a large materialized read model, an index-backed page query
/// and the ordered-id snapshot stay fast (matching the spike envelope). Seeded to 20k here to keep the
/// test quick; the spike proved the shape to 1M. (Checklist 1.42.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class ReadPathPerfTests
{
    private const int Rows = 20_000;

    [Fact]
    public async Task Paged_query_and_ordered_ids_are_fast_on_a_large_catalog()
    {
        using var fx = new SqliteTestDatabase();
        using (var db = fx.NewContext())
        {
            var packages = new List<Package>(Rows);
            var items = new List<PackageListItem>(Rows);
            for (var i = 0; i < Rows; i++)
            {
                var varName = $"Creator{i % 500}.Package{i}.1";
                packages.Add(new Package
                {
                    Id = i + 1, VarName = varName, IdentityKey = varName.ToUpperInvariant(),
                    Creator = $"Creator{i % 500}", PackageName = $"Package{i}", VersionToken = "1", VersionSort = 1,
                    FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
                });
                items.Add(new PackageListItem
                {
                    PackageId = i + 1,
                    VarName = varName,
                    Creator = $"Creator{i % 500}",
                    PackageName = $"Package{i}",
                    VersionToken = "1",
                    PrimaryType = ContentType.Scene,
                    TotalSize = i * 1000L,
                    OnlineInstanceCount = 1,
                    TotalInstanceCount = 1,
                    Class = ContentClass.Cold,
                    AddedAt = DateTime.UtcNow,
                });
            }
            db.Packages.AddRange(packages);
            await db.SaveChangesAsync();
            db.PackageListItems.AddRange(items);
            await db.SaveChangesAsync();
        }

        using var query = fx.NewContext();
        var service = new EfLibraryQueryService(query);

        // A deep page must not degrade (index-backed sort + keyset-style paging).
        var sw = Stopwatch.StartNew();
        var page = await service.GetPageAsync(new LibraryQuery(Skip: 15_000, Take: 100, Sort: LibrarySort.Creator));
        sw.Stop();
        Assert.Equal(100, page.Items.Count);
        Assert.Equal(Rows, page.TotalCount);
        Assert.True(sw.ElapsedMilliseconds < 500, $"paging took {sw.ElapsedMilliseconds} ms");

        // The full ordered-id list (OrderedSnapshot backbone) materializes quickly.
        sw.Restart();
        var ids = await service.GetOrderedIdsAsync(new LibraryQuery(Sort: LibrarySort.Name));
        sw.Stop();
        Assert.Equal(Rows, ids.Count);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"ordered ids took {sw.ElapsedMilliseconds} ms");
    }
}
