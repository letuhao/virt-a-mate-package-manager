using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Analyzer;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Integration)]
public sealed class UsageRecordManyTests
{
    [Fact]
    public async Task RecordMany_dedupes_and_saves_one_event_per_package()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var usage = scope.ServiceProvider.GetRequiredService<IUsageAnalyzer>();

        var a = await SeedPackageAsync(db, "A.One.1");
        var b = await SeedPackageAsync(db, "B.Two.1");

        await usage.RecordManyAsync([a, b, a, 0, b], UsageKind.Activate);

        var events = await db.UsageEvents.AsNoTracking().OrderBy(e => e.PackageId).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(UsageKind.Activate, e.Kind));
        Assert.All(events, e => Assert.Equal(UsageSource.AppObserved, e.Source));
        Assert.Contains(events, e => e.PackageId == a);
        Assert.Contains(events, e => e.PackageId == b);
    }

    [Fact]
    public async Task RecordMany_empty_is_noop()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var usage = scope.ServiceProvider.GetRequiredService<IUsageAnalyzer>();
        await usage.RecordManyAsync([], UsageKind.Activate);
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        Assert.Empty(await db.UsageEvents.ToListAsync());
    }

    [Fact]
    public async Task RecomputePackages_only_updates_requested_ids()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var usage = scope.ServiceProvider.GetRequiredService<IUsageAnalyzer>();

        var a = await SeedPackageAsync(db, "A.One.1");
        var b = await SeedPackageAsync(db, "B.Two.1");
        await usage.RecordManyAsync([a, b], UsageKind.Activate);

        Assert.Equal(1, await usage.RecomputePackagesAsync([a]));
        Assert.NotNull(await db.UsageStats.AsNoTracking().FirstOrDefaultAsync(s => s.PackageId == a));
        Assert.Null(await db.UsageStats.AsNoTracking().FirstOrDefaultAsync(s => s.PackageId == b));
    }

    [Fact]
    public async Task RecomputePackages_batch_updates_many_packages()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var usage = scope.ServiceProvider.GetRequiredService<IUsageAnalyzer>();

        const int count = 500;
        var ids = new List<long>(count);
        for (var i = 0; i < count; i++)
            ids.Add(await SeedPackageAsync(db, $"C.Pkg{i}.{i}"));

        await usage.RecordManyAsync(ids, UsageKind.Activate);
        Assert.Equal(count, await usage.RecomputePackagesAsync(ids));

        var stats = await db.UsageStats.AsNoTracking().Where(s => ids.Contains(s.PackageId)).ToListAsync();
        Assert.Equal(count, stats.Count);
        Assert.All(stats, s =>
        {
            Assert.True(s.UseCountTotal >= 1);
            Assert.NotNull(s.LastUsedAt);
        });
    }

    [Fact]
    public async Task RecordManyAndRecomputeAsync_records_and_scores_in_one_pass()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var usage = scope.ServiceProvider.GetRequiredService<IUsageAnalyzer>();

        var a = await SeedPackageAsync(db, "D.One.1");
        var b = await SeedPackageAsync(db, "D.Two.1");

        await usage.RecordManyAndRecomputeAsync([a, b], UsageKind.Activate);

        Assert.Equal(2, await db.UsageEvents.AsNoTracking().CountAsync());
        var stats = await db.UsageStats.AsNoTracking().Where(s => s.PackageId == a || s.PackageId == b).ToListAsync();
        Assert.Equal(2, stats.Count);
        Assert.All(stats, s => Assert.True(s.UseCountTotal >= 1));
    }

    [Fact]
    public async Task RecomputePackages_chunked_handles_over_1000_ids()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var usage = scope.ServiceProvider.GetRequiredService<IUsageAnalyzer>();

        const int count = 1200;
        var ids = new List<long>(count);
        for (var i = 0; i < count; i++)
            ids.Add(await SeedPackageAsync(db, $"F.Pkg{i}.{i}"));

        await usage.RecordManyAsync(ids, UsageKind.Activate);
        Assert.Equal(count, await usage.RecomputePackagesAsync(ids));

        Assert.Equal(count, await db.UsageStats.AsNoTracking().CountAsync(s => ids.Contains(s.PackageId)));
    }

    [Fact]
    public async Task Catalog_writer_persists_inside_write_queue_scope()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var writeQueue = scope.ServiceProvider.GetRequiredService<VarVault.Sdk.Threading.IWriteQueue>();

        var a = await SeedPackageAsync(db, "E.One.1");
        var b = await SeedPackageAsync(db, "E.Two.1");
        var ids = new List<long> { a, b };

        await writeQueue.EnqueueScopedAsync(async (sp, ct) =>
        {
            await sp.GetRequiredService<IUsageCatalogWriter>()
                .RecordManyAndRecomputeAsync(ids, UsageKind.Activate, ct)
                .ConfigureAwait(false);
        }, VarVault.Sdk.Threading.WritePriority.Bulk, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(2, await db.UsageEvents.AsNoTracking().CountAsync());
    }

    private static async Task<long> SeedPackageAsync(VarVaultDbContext db, string varName)
    {
        var parts = varName.Split('.');
        var pkg = new Package
        {
            VarName = varName,
            IdentityKey = varName.ToUpperInvariant(),
            Creator = parts[0],
            PackageName = parts[1],
            VersionToken = parts[2],
            VersionSort = 1,
            FirstSeenAt = DateTime.UtcNow,
            LastIndexedAt = DateTime.UtcNow,
        };
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();
        return pkg.Id;
    }
}
