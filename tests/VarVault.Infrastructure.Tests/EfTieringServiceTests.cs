using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Library;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>BE-N2 · Tiering: class counts, misplaced detection, propose-only migration plan. (16-checklist BE-N2.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class EfTieringServiceTests
{
    private static readonly Guid T1 = Guid.NewGuid();
    private static readonly Guid T3 = Guid.NewGuid();

    private static FakeRepos Repos() => new(
    [
        new RepositoryInfo(T1, "T1", @"C:\t1", "NVMe", 1, IsOnline: true, IsEnabled: true, 2000, 600, null),
        new RepositoryInfo(T3, "T3", @"D:\t3", "HDD", 3, IsOnline: true, IsEnabled: true, 8000, 5000, null),
    ]);

    [Fact]
    public async Task Class_counts_and_misplaced_from_read_model()
    {
        using var fx = new SqliteTestDatabase();
        using (var db = fx.NewContext())
        {
            // hot on tier 3 (misplaced → should be 1); cold on tier 1 (misplaced → 3); warm on 2 (correct)
            SeedItem(db, 1, "A.Hot.1", ContentClass.Hot, actualTier: 3, size: 800);
            SeedItem(db, 2, "A.Cold.1", ContentClass.Cold, actualTier: 1, size: 3400);
            SeedItem(db, 3, "A.Warm.1", ContentClass.Warm, actualTier: 2, size: 500);
            await db.SaveChangesAsync();
        }

        using var read = fx.NewContext();
        var svc = new EfTieringService(read, Repos());

        var counts = await svc.ClassCountsAsync();
        Assert.Equal(1, counts.Hot);
        Assert.Equal(1, counts.Warm);
        Assert.Equal(1, counts.Cold);

        var misplaced = await svc.MisplacedAsync();
        Assert.Equal(2, misplaced.Count); // hot + cold, not the warm one
        var hot = misplaced.Single(m => m.VarName == "A.Hot.1");
        Assert.Equal(3, hot.CurrentTier);
        Assert.Equal(1, hot.DesiredTier);
    }

    [Fact]
    public async Task Build_plan_proposes_moves_and_excludes_single_copy()
    {
        using var fx = new SqliteTestDatabase();
        using (var db = fx.NewContext())
        {
            db.Repositories.Add(NewRepo(T1, "T1", 1));
            db.Repositories.Add(NewRepo(T3, "T3", 3));
            // Hot package with 2 online copies on T3 → proposable to T1.
            SeedItem(db, 1, "A.Hot.1", ContentClass.Hot, actualTier: 3, size: 800, online: 2);
            db.VarFiles.Add(NewVar(10, 1, T3));
            db.VarFiles.Add(NewVar(11, 1, T3));
            // Cold single-copy on T1 → misplaced but excluded (single copy).
            SeedItem(db, 2, "A.Cold.1", ContentClass.Cold, actualTier: 1, size: 3400, online: 1);
            db.VarFiles.Add(NewVar(20, 2, T1));
            await db.SaveChangesAsync();
        }

        using var read = fx.NewContext();
        var plan = await new EfTieringService(read, Repos()).BuildPlanAsync();

        Assert.Contains(plan.Proposals, p => p.FromTier == 3 && p.ToTier == 1); // hot copies move up
        Assert.True(plan.ExcludedCount >= 1); // the single-copy cold var excluded
    }

    [Fact]
    public async Task Policy_reports_the_active_class_to_tier_map()
    {
        using var fx = new SqliteTestDatabase();
        using var read = fx.NewContext();
        var policy = await new EfTieringService(read, Repos()).PolicyAsync();

        Assert.Equal(3, policy.Placements.Count);
        var hot = policy.Placements.Single(p => p.StorageClass == "Hot").DesiredTier;
        var cold = policy.Placements.Single(p => p.StorageClass == "Cold").DesiredTier;
        Assert.True(hot < cold, "Hot must place on a faster (lower) tier than Cold"); // reflects the real PlacementPolicy
    }

    [Fact]
    public async Task Stale_lists_only_superseded_cold_versions()
    {
        using var fx = new SqliteTestDatabase();
        using (var db = fx.NewContext())
        {
            // Same family (Acme.Thing): v1 cold + superseded, v2 hot + latest → only v1 is stale.
            SeedVersioned(db, 1, "Acme.Thing.1", "Acme", "Thing", sort: 1, ContentClass.Cold);
            SeedVersioned(db, 2, "Acme.Thing.2", "Acme", "Thing", sort: 2, ContentClass.Hot);
            // Single-version cold package → not stale (no newer version).
            SeedVersioned(db, 3, "Acme.Solo.1", "Acme", "Solo", sort: 1, ContentClass.Cold);
            // Superseded but Hot → not stale (only cold superseded versions count).
            SeedVersioned(db, 4, "Acme.Warm.1", "Acme", "Warm", sort: 1, ContentClass.Hot);
            SeedVersioned(db, 5, "Acme.Warm.2", "Acme", "Warm", sort: 2, ContentClass.Hot);
            await db.SaveChangesAsync();
        }

        using var read = fx.NewContext();
        var stale = await new EfTieringService(read, Repos()).StaleVersionsAsync();

        Assert.Single(stale);
        Assert.Equal(1, stale[0].PackageId);
    }

    private static void SeedVersioned(VarVaultDbContext db, long id, string varName, string creator, string pkg, int sort, ContentClass cls)
    {
        db.Packages.Add(new Package
        {
            Id = id, VarName = varName, IdentityKey = varName.ToUpperInvariant(), Creator = creator, PackageName = pkg,
            VersionToken = sort.ToString(), VersionSort = sort, FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        });
        db.PackageListItems.Add(new PackageListItem
        {
            PackageId = id, VarName = varName, Creator = creator, PackageName = pkg, VersionToken = sort.ToString(),
            PrimaryType = ContentType.Scene, TotalSize = 100, Class = cls, ActualTierMin = 3,
            OnlineInstanceCount = 1, IsSingleCopy = true, AddedAt = DateTime.UtcNow,
        });
    }

    private static void SeedItem(VarVaultDbContext db, long id, string name, ContentClass cls, int actualTier, long size, int online = 1)
    {
        db.Packages.Add(new Package
        {
            Id = id, VarName = name, IdentityKey = name.ToUpperInvariant(), Creator = "A", PackageName = name,
            VersionToken = "1", VersionSort = 1, FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        });
        db.PackageListItems.Add(new PackageListItem
        {
            PackageId = id, VarName = name, Creator = "A", PackageName = name, VersionToken = "1",
            PrimaryType = ContentType.Scene, TotalSize = size, Class = cls, ActualTierMin = actualTier,
            OnlineInstanceCount = online, IsSingleCopy = online <= 1, AddedAt = DateTime.UtcNow,
        });
    }

    private static Repository NewRepo(Guid id, string name, int tier) => new()
    {
        Id = id, Name = name, MountPath = $@"X:\{name}", Tier = tier, IsOnline = true, IsEnabled = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static VarFile NewVar(long id, long pkgId, Guid repoId) => new()
    {
        Id = id, PackageId = pkgId, RepositoryId = repoId, RelativePath = $"v{id}.var",
        SizeBytes = 100, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
    };

    private sealed class FakeRepos(IReadOnlyList<RepositoryInfo> list) : IRepositoryService
    {
        public Task<IReadOnlyList<RepositoryInfo>> ListAsync(CancellationToken ct = default) => Task.FromResult(list);
        public Task<Result<RepositoryInfo>> RegisterAsync(RegisterRepositoryRequest r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SetEnabledAsync(Guid id, bool e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RepositoryInfo?> RefreshCapacityAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<RepositoryInfo>> RepointAsync(Guid id, string p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RepositoryInfo?> BenchmarkAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RepositoryInfo?> SetTierAsync(Guid id, int tier, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
