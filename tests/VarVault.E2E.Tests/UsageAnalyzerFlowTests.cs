using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Analyzer;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// End-to-end usage analysis: record usage events, recompute classifications, and see the UsageStat +
/// read-model class update — with the FakeClock proving windows are time-relative. (5.1/5.2, BE-A1/A6.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class UsageAnalyzerFlowTests
{
    [Fact]
    public async Task Records_events_and_recomputes_classification()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "A.Thing.1.var", "A", "Thing");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        long packageId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            packageId = (await db.Packages.FirstAsync()).Id;

            var usage = scope.ServiceProvider.GetRequiredService<IUsageAnalyzer>();
            for (var i = 0; i < 12; i++)
                await usage.RecordAsync(packageId, UsageKind.Load);

            Assert.Equal(1, await usage.RecomputeAsync());
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            var stat = await db.UsageStats.FirstAsync(s => s.PackageId == packageId);
            Assert.Equal(12, stat.UseCountTotal);
            Assert.Equal(12, stat.Use30d);
            Assert.NotNull(stat.LastUsedAt);
            Assert.Equal(ContentClass.Hot, stat.Class); // frequent recent use → hot

            var item = await db.PackageListItems.FirstAsync(x => x.PackageId == packageId);
            Assert.Equal(ContentClass.Hot, item.Class); // read model reflects it
        }
    }

    [Fact]
    public async Task Windows_cool_as_the_clock_advances_with_no_new_events()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "A.Thing.1.var", "A", "Thing");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var packageId = (await db.Packages.FirstAsync()).Id;
        var usage = scope.ServiceProvider.GetRequiredService<IUsageAnalyzer>();

        await usage.RecordAsync(packageId, UsageKind.Load);
        await usage.RecomputeAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(1, (await db.UsageStats.FirstAsync(s => s.PackageId == packageId)).Use30d);

        // 40 days later, with no new events, the event has aged out of the 30-day window.
        host.Clock.Advance(TimeSpan.FromDays(40));
        await usage.RecomputeAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(0, (await db.UsageStats.FirstAsync(s => s.PackageId == packageId)).Use30d);
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

    private static void WriteVar(TempDirectory dir, string fileName, string creator, string package)
    {
        var path = Path.Combine(dir.Path, fileName);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("meta.json", CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes("{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\"}"));
    }
}
