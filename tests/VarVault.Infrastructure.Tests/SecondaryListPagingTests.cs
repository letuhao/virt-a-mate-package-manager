using System.Diagnostics;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Library;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Secondary-list paging correctness + scale: first/middle/last pages, totals, no overlap, and a
/// 70k-row integrity catalog that returns at most PageSize. (A16 / pagination verify.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class SecondaryListPagingTests
{
    private static readonly Guid RepoId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task Missing_deps_pages_are_stable_ordered_and_non_overlapping()
    {
        using var fx = new SqliteTestDatabase();
        using (var db = fx.NewContext())
        {
            AddRepo(db);
            for (var i = 1; i <= 5; i++)
            {
                db.Packages.Add(new Package
                {
                    Id = i, VarName = $"Owner.Pkg{i}.1", IdentityKey = $"OWNER.PKG{i}.1",
                    Creator = "Owner", PackageName = $"Pkg{i}", VersionToken = "1", VersionSort = 1,
                    FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
                });
                db.VarFiles.Add(new VarFile
                {
                    Id = i, PackageId = i, RepositoryId = RepoId, RelativePath = $"{i}.var",
                    SizeBytes = 10, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
                });
            }

            // Ghost.A needed by 3 pkgs; Ghost.B by 2; Ghost.C by 1 — plus filler refs for paging.
            AddMissing(db, 1, "Ghost.A.1");
            AddMissing(db, 2, "Ghost.A.1");
            AddMissing(db, 3, "Ghost.A.1");
            AddMissing(db, 1, "Ghost.B.1");
            AddMissing(db, 2, "Ghost.B.1");
            AddMissing(db, 1, "Ghost.C.1");
            for (var i = 1; i <= 60; i++)
                AddMissing(db, 1, $"Ghost.Fill{i:D3}.1");
            await db.SaveChangesAsync();
        }

        using var read = fx.NewContext();
        var query = new EfMissingDepsQuery(read);

        var first = await query.GetPageAsync(new PageRequest(1, 25));
        var middle = await query.GetPageAsync(new PageRequest(2, 25));
        var last = await query.GetPageAsync(new PageRequest(3, 25));
        var empty = await query.GetPageAsync(new PageRequest(99, 25));

        Assert.Equal(63, first.TotalCount);
        Assert.Equal(25, first.Items.Count);
        Assert.Equal(25, middle.Items.Count);
        Assert.Equal(13, last.Items.Count);
        Assert.Empty(empty.Items);
        Assert.Equal("Ghost.A.1", first.Items[0].Ref);
        Assert.Equal(3, first.Items[0].NeededByCount);

        var seen = first.Items.Concat(middle.Items).Concat(last.Items).Select(x => x.Ref).ToList();
        Assert.Equal(seen.Count, seen.Distinct(StringComparer.Ordinal).Count());
        Assert.True(first.Items.Zip(first.Items.Skip(1)).All(p =>
            p.First.NeededByCount > p.Second.NeededByCount
            || (p.First.NeededByCount == p.Second.NeededByCount
                && string.CompareOrdinal(p.First.Ref, p.Second.Ref) < 0)));
    }

    [Fact]
    public async Task Integrity_page_on_70k_rows_returns_at_most_page_size_and_stays_fast()
    {
        const int Rows = 70_000;
        using var fx = new SqliteTestDatabase();
        using (var db = fx.NewContext())
        {
            AddRepo(db);
            var batch = new List<VarFile>(10_000);
            for (var i = 1; i <= Rows; i++)
            {
                batch.Add(new VarFile
                {
                    Id = i,
                    PackageId = null,
                    RepositoryId = RepoId,
                    RelativePath = $"corrupt/{i}.var",
                    SizeBytes = 100,
                    FileMtime = DateTime.UtcNow,
                    IndexedAt = DateTime.UtcNow,
                    IntegrityStatus = IntegrityStatus.CorruptZip,
                });
                if (batch.Count == 10_000)
                {
                    db.VarFiles.AddRange(batch);
                    await db.SaveChangesAsync();
                    db.ChangeTracker.Clear();
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
            {
                db.VarFiles.AddRange(batch);
                await db.SaveChangesAsync();
            }
        }

        using var read = fx.NewContext();
        var health = new EfHealthService(read, null!);

        var sw = Stopwatch.StartNew();
        var page = await health.IntegrityPageAsync(new PageRequest(2, 50));
        sw.Stop();

        Assert.Equal(50, page.Items.Count);
        Assert.True(page.Items.Count <= page.PageSize);
        Assert.Equal(Rows, page.TotalCount);
        Assert.Equal(51, page.Items[0].VarFileId); // OrderBy Id, page 2 of size 50
        Assert.True(sw.ElapsedMilliseconds < 1500, $"integrity page took {sw.ElapsedMilliseconds} ms");

        // Changing page size must still bound the materialization.
        var tiny = await health.IntegrityPageAsync(new PageRequest(1, 25));
        Assert.Equal(25, tiny.Items.Count);
        Assert.Equal(Rows, tiny.TotalCount);
    }

    [Fact]
    public async Task Activity_pages_newest_first_with_exact_totals()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();
        var clock = new FakeClock();
        var log = new EfActivityLog(db, clock);
        for (var i = 0; i < 120; i++)
        {
            await log.RecordAsync("fix", $"n={i}");
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        var page = await log.GetRecentPageAsync(new PageRequest(1, 50));
        Assert.Equal(50, page.Items.Count);
        Assert.Equal(120, page.TotalCount);
        Assert.Equal("n=119", page.Items[0].Description);
        Assert.Equal("n=70", page.Items[^1].Description);

        var last = await log.GetRecentPageAsync(new PageRequest(3, 50));
        Assert.Equal(20, last.Items.Count);
        Assert.Equal("n=0", last.Items[^1].Description);
    }

    private static void AddRepo(VarVaultDbContext db) => db.Repositories.Add(new Repository
    {
        Id = RepoId, Name = "r", MountPath = @"X:\r", Tier = 3, IsOnline = true, IsEnabled = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    });

    private static void AddMissing(VarVaultDbContext db, long varFileId, string raw) =>
        db.Dependencies.Add(new Dependency
        {
            VarFileId = varFileId,
            DependsOnRefRaw = raw,
            DependsOnRefKey = raw.ToUpperInvariant(),
            IsMissing = true,
        });
}
