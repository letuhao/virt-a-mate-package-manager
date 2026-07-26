using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Analyzer;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Integration)]
public sealed class PlacementOverrideTests
{
    [Fact]
    public async Task Pin_hot_sets_class_Hot_without_usage_events()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        db.Packages.Add(new Package
        {
            Id = 1, VarName = "A.B.1", IdentityKey = "A.B.1", Creator = "A", PackageName = "B",
            VersionToken = "1", VersionSort = 1, FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        });
        db.PackageListItems.Add(new PackageListItem
        {
            PackageId = 1, VarName = "A.B.1", Creator = "A", PackageName = "B", VersionToken = "1",
            Class = ContentClass.Cold, TotalSize = 1,
        });
        await db.SaveChangesAsync();

        var usage = scope.ServiceProvider.GetRequiredService<IUsageAnalyzer>();
        await usage.SetPlacementOverridesAsync(1, pinHot: true, forceCold: false);

        var stat = await db.UsageStats.AsNoTracking().SingleAsync(s => s.PackageId == 1);
        Assert.True(stat.IsPinnedHot);
        Assert.Equal(ContentClass.Hot, stat.Class);
        var item = await db.PackageListItems.AsNoTracking().SingleAsync(i => i.PackageId == 1);
        Assert.Equal(ContentClass.Hot, item.Class);
    }

    [Fact]
    public async Task Force_cold_clears_pin_and_sets_Cold()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var usage = scope.ServiceProvider.GetRequiredService<IUsageAnalyzer>();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        db.Packages.Add(new Package
        {
            Id = 2, VarName = "C.D.1", IdentityKey = "C.D.1", Creator = "C", PackageName = "D",
            VersionToken = "1", VersionSort = 1, FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await usage.SetPlacementOverridesAsync(2, pinHot: true, forceCold: false);
        await usage.SetPlacementOverridesAsync(2, pinHot: false, forceCold: true);

        var stat = await db.UsageStats.AsNoTracking().SingleAsync(s => s.PackageId == 2);
        Assert.False(stat.IsPinnedHot);
        Assert.True(stat.IsForcedCold);
        Assert.Equal(ContentClass.Cold, stat.Class);
    }
}
