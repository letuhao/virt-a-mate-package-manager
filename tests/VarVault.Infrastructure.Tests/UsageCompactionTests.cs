using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Event rollup compaction: events older than the largest window fold into a per-package rollup count
/// (UseCountTotal preserved), and the raw rows are deleted. (5.3, BE-A7.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class UsageCompactionTests
{
    [Fact]
    public async Task Compacts_old_events_into_a_rollup_preserving_total()
    {
        using var fx = new SqliteTestDatabase();
        var clock = new FakeClock();
        var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();

        long packageId;
        using (var db = fx.NewContext())
        {
            var pkg = new Package
            {
                VarName = "C.P.1", IdentityKey = "C.P.1", Creator = "C", PackageName = "P",
                VersionToken = "1", VersionSort = 1, FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
            };
            db.Packages.Add(pkg);
            await db.SaveChangesAsync();
            packageId = pkg.Id;

            // 3 old events (120 days ago) + 2 recent.
            for (var i = 0; i < 3; i++)
                db.UsageEvents.Add(new UsageEvent { PackageId = packageId, TimestampUnixMs = nowMs - (120L * 86_400_000), Kind = UsageKind.Load });
            for (var i = 0; i < 2; i++)
                db.UsageEvents.Add(new UsageEvent { PackageId = packageId, TimestampUnixMs = nowMs - (5L * 86_400_000), Kind = UsageKind.Load });
            await db.SaveChangesAsync();
        }

        using (var db = fx.NewContext())
        {
            var analyzer = new EfUsageAnalyzer(db, clock);
            var compacted = await analyzer.CompactAsync(olderThanDays: 90);
            Assert.Equal(3, compacted);

            await analyzer.RecomputeAsync();
        }

        using (var db = fx.NewContext())
        {
            Assert.Equal(2, await db.UsageEvents.CountAsync()); // only recent events remain
            var stat = await db.UsageStats.FirstAsync(s => s.PackageId == packageId);
            Assert.Equal(3, stat.RolledUpUseCount);
            Assert.Equal(5, stat.UseCountTotal); // 3 rolled up + 2 live — total preserved
        }
    }

    [Fact]
    public async Task Nothing_to_compact_returns_zero()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();
        var analyzer = new EfUsageAnalyzer(db, new FakeClock());
        Assert.Equal(0, await analyzer.CompactAsync());
    }
}
