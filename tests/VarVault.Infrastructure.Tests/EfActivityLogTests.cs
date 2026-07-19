using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>The activity log records audited actions and reads them newest-first. (X.12.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class EfActivityLogTests
{
    [Fact]
    public async Task Records_and_reads_newest_first()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();
        var clock = new FakeClock();
        var log = new EfActivityLog(db, clock);

        await log.RecordAsync("Index", "Indexed repo A");
        clock.Advance(TimeSpan.FromSeconds(1));
        await log.RecordAsync("Migrate", "Moved A.B.1 to Tier 1");
        clock.Advance(TimeSpan.FromSeconds(1));
        await log.RecordAsync("Delete", "Trashed a duplicate");

        var recent = await log.GetRecentAsync();
        Assert.Equal(3, recent.Count);
        Assert.Equal("Delete", recent[0].Kind);  // newest first
        Assert.Equal("Index", recent[2].Kind);
    }

    [Fact]
    public async Task Limit_caps_the_result()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();
        var log = new EfActivityLog(db, new FakeClock());
        for (var i = 0; i < 5; i++)
            await log.RecordAsync("Fix", $"fix {i}");

        Assert.Equal(2, (await log.GetRecentAsync(limit: 2)).Count);
    }
}
