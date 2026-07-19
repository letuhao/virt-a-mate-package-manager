using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Library;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>BE-N1 · Dashboard summary aggregates the read model + repositories. (16-checklist BE-N1.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class EfDashboardServiceTests
{
    [Fact]
    public async Task Summary_aggregates_totals_classes_and_tiers()
    {
        using var fx = new SqliteTestDatabase();
        using (var db = fx.NewContext())
        {
            Seed(db, id: 1, "A.P1.1", ContentClass.Hot, size: 1000, active: true, missing: false);
            Seed(db, id: 2, "A.P2.1", ContentClass.Hot, size: 2000, active: false, missing: true);
            Seed(db, id: 3, "A.P3.1", ContentClass.Warm, size: 500, active: false, missing: false);
            Seed(db, id: 4, "A.P4.1", ContentClass.Cold, size: 4000, active: false, missing: false);
            await db.SaveChangesAsync();
        }

        var repos = new FakeRepos(
        [
            new RepositoryInfo(Guid.NewGuid(), "T1", @"C:\t1", "NVMe", 1, IsOnline: true, IsEnabled: true, CapacityBytes: 2000, FreeBytes: 600, VolumeSerial: null),
            new RepositoryInfo(Guid.NewGuid(), "T3", @"D:\t3", "HDD", 3, IsOnline: false, IsEnabled: true, CapacityBytes: 8000, FreeBytes: 5000, VolumeSerial: null),
        ]);

        using var read = fx.NewContext();
        var svc = new EfDashboardService(read, repos);
        var s = await svc.GetSummaryAsync();

        Assert.Equal(4, s.TotalPackages);
        Assert.Equal(7500, s.TotalBytes);
        Assert.Equal(2, s.HotCount);
        Assert.Equal(1, s.WarmCount);
        Assert.Equal(1, s.ColdCount);
        Assert.Equal(1, s.MissingDepsCount);
        Assert.Equal(1, s.ActiveInGame);
        Assert.Equal(2, s.RepositoryCount);
        Assert.Equal(1, s.OfflineRepositoryCount);
        Assert.Equal(2, s.Tiers.Count);
        Assert.Equal(1400, s.Tiers[0].UsedBytes);   // T1: 2000-600
        Assert.Equal(2000, s.Tiers[0].CapacityBytes);
        Assert.Equal(3000, s.Tiers[1].UsedBytes);   // T3: 8000-5000
    }

    [Fact]
    public async Task Empty_catalog_returns_zeros()
    {
        using var fx = new SqliteTestDatabase();
        using var read = fx.NewContext();
        var s = await new EfDashboardService(read, new FakeRepos([])).GetSummaryAsync();
        Assert.Equal(0, s.TotalPackages);
        Assert.Equal(0, s.TotalBytes);
        Assert.Empty(s.Tiers);
    }

    private static void Seed(VarVaultDbContext db, long id, string name, ContentClass cls, long size, bool active, bool missing)
    {
        db.Packages.Add(new Package
        {
            Id = id, VarName = name, IdentityKey = name.ToUpperInvariant(), Creator = "A", PackageName = name,
            VersionToken = "1", VersionSort = 1, FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        });
        db.PackageListItems.Add(new PackageListItem
        {
            PackageId = id, VarName = name, Creator = "A", PackageName = name, VersionToken = "1",
            PrimaryType = ContentType.Scene, TotalSize = size, Class = cls, IsActive = active,
            HasMissingDeps = missing, AddedAt = DateTime.UtcNow,
        });
    }

    private sealed class FakeRepos(IReadOnlyList<RepositoryInfo> list) : IRepositoryService
    {
        public Task<IReadOnlyList<RepositoryInfo>> ListAsync(CancellationToken ct = default) => Task.FromResult(list);
        public Task<Result<RepositoryInfo>> RegisterAsync(RegisterRepositoryRequest r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SetEnabledAsync(Guid id, bool e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RepositoryInfo?> RefreshCapacityAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<RepositoryInfo>> RepointAsync(Guid id, string p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RepositoryInfo?> BenchmarkAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
