using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Library;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Bulk read-model refresh runs inside a single transaction and rebuilds the FTS index once; every
/// refreshed package is materialized and searchable afterwards, at an acceptable rate. (Checklist 1.25.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class BulkRefreshFtsTests
{
    private const int Rows = 400;

    [Fact]
    public async Task Bulk_refresh_materializes_all_rows_and_rebuilds_the_search_index()
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
            for (var i = 1; i <= Rows; i++)
            {
                var name = $"Creator{i}.Pkg{i}.1";
                db.Packages.Add(new Package
                {
                    Id = i, VarName = name, IdentityKey = name.ToUpperInvariant(),
                    Creator = $"Creator{i}", PackageName = $"Pkg{i}", VersionToken = "1", VersionSort = 1,
                    FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
                });
                db.VarFiles.Add(new VarFile
                {
                    Id = i, PackageId = i, RepositoryId = repoId, RelativePath = $"{name}.var",
                    SizeBytes = 1000, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync();
        }

        var ids = Enumerable.Range(1, Rows).Select(i => (long)i).ToList();

        var sw = Stopwatch.StartNew();
        using (var writeDb = fx.NewContext())
        {
            var store = new EfCatalogStore(writeDb, new FakeClock());
            await store.RefreshReadModelAsync(ids); // one batched transaction, FTS rebuilt once
        }
        sw.Stop();

        using (var read = fx.NewContext())
        {
            // Every package materialized into the read model.
            Assert.Equal(Rows, await read.PackageListItems.CountAsync());

            // The whole FTS index was rebuilt: every package is searchable by its creator.
            var query = new EfLibraryQueryService(read);
            // Prefer a token that does not prefix-match Creator70–79 under FTS.
            var hit = await query.GetPageAsync(new LibraryQuery(SearchText: "Creator7.Pkg7", Take: 10));
            Assert.Contains(hit.Items, p => p.Creator == "Creator7");

            // A search that spans many rows returns them all (index is complete, not partial).
            var all = await read.Database
                .SqlQueryRaw<long>("SELECT rowid AS \"Value\" FROM PackageSearch WHERE Blob MATCH {0}", "Pkg")
                .ToListAsync();
            Assert.Equal(Rows, all.Count);
        }

        // Throughput sanity (T/B): a single transaction keeps the rate reasonable, not one commit per row.
        Assert.True(sw.ElapsedMilliseconds < 20_000, $"bulk refresh of {Rows} took {sw.ElapsedMilliseconds} ms");
    }
}
