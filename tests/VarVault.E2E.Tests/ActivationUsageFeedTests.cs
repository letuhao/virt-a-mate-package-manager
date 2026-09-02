using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Activation;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Presets;
using VarVault.Sdk.Settings;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// G1 · Successful loading-preset activation appends <see cref="UsageKind.Activate"/> events and
/// recomputes usage stats (the only production usage feed in v1).
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ActivationUsageFeedTests
{
    [Fact]
    public async Task Activate_records_usage_events_for_install_set_and_second_activate_increments()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "A.Look.1.var", "A", "Look", "A.Base.1");
        WriteVar(repoDir, "A.Base.1.var", "A", "Base");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>().SetAsync(SettingKeys.VamPath, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<VarVault.Domain.Dependencies.IDependencyResolver>().ResolveAllAsync();

        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>()
            .CreateAsync("P", ["A.Look.1"])).Value;
        var activation = scope.ServiceProvider.GetRequiredService<IActivationService>();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();

        var first = await activation.BuildProfileLinksAsync(preset.Id);
        if (first.PrivilegeFailures > 0)
            return; // no Developer Mode — cannot materialize; usage hook runs only after success

        Assert.Equal(2, first.LinksCreated);

        var look = await db.Packages.AsNoTracking().FirstAsync(p => p.VarName == "A.Look.1");
        var baseP = await db.Packages.AsNoTracking().FirstAsync(p => p.VarName == "A.Base.1");

        var events1 = await db.UsageEvents.AsNoTracking().ToListAsync();
        Assert.Equal(2, events1.Count);
        Assert.All(events1, e => Assert.Equal(UsageKind.Activate, e.Kind));
        Assert.Contains(events1, e => e.PackageId == look.Id);
        Assert.Contains(events1, e => e.PackageId == baseP.Id);

        var lookStat = await db.UsageStats.AsNoTracking().FirstAsync(s => s.PackageId == look.Id);
        Assert.True(lookStat.UseCountTotal >= 1);
        Assert.NotNull(lookStat.LastUsedAt);

        var second = await activation.BuildProfileLinksAsync(preset.Id);
        Assert.Equal(0, second.PrivilegeFailures);

        db.ChangeTracker.Clear();
        var events2 = await db.UsageEvents.AsNoTracking().ToListAsync();
        Assert.Equal(4, events2.Count); // 2 packages × 2 activates
        Assert.Equal(2, await db.UsageEvents.CountAsync(e => e.PackageId == look.Id));
        Assert.Equal(2, (await db.UsageStats.FirstAsync(s => s.PackageId == look.Id)).UseCountTotal);

        // Deactivate rebuild must not append more Activate events.
        var beforeDeact = await db.UsageEvents.CountAsync();
        await activation.DeactivateAsync(preset.Id, look.Id);
        db.ChangeTracker.Clear();
        Assert.Equal(beforeDeact, await db.UsageEvents.CountAsync());
    }

    [Fact]
    public async Task Activate_does_not_share_dbcontext_with_usage_recompute()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "A.Look.1.var", "A", "Look", "A.Base.1");
        WriteVar(repoDir, "A.Base.1.var", "A", "Base");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>().SetAsync(SettingKeys.VamPath, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<VarVault.Domain.Dependencies.IDependencyResolver>().ResolveAllAsync();

        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>()
            .CreateAsync("P", ["A.Look.1"])).Value;
        var activation = scope.ServiceProvider.GetRequiredService<IActivationService>();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();

        var first = await activation.BuildProfileLinksAsync(preset.Id);
        if (first.PrivilegeFailures > 0)
            return;

        // Second activation enqueues scoped usage writes + background recompute.
        // Sequential reads on the caller context must stay healthy (no shared-context crash).
        await activation.BuildProfileLinksAsync(preset.Id);

        for (var i = 0; i < 40; i++)
        {
            _ = await db.Packages.AsNoTracking().CountAsync();
            if (await db.UsageEvents.AsNoTracking().CountAsync() >= 4)
                break;
            await Task.Delay(50);
        }

        Assert.True(await db.UsageEvents.AsNoTracking().CountAsync() >= 4);
        _ = await db.UsageStats.AsNoTracking().CountAsync();
    }

    private static async Task<Guid> Register(TestHost host, string path)
    {
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var id = Guid.NewGuid();
        db.Repositories.Add(new Repository
        {
            Id = id, Name = "t", MountPath = path, IsOnline = true, IsEnabled = true, Tier = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static void WriteVar(TempDirectory dir, string fileName, string creator, string package, params string[] deps)
    {
        var path = Path.Combine(dir.Path, fileName);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var depObj = string.Join(",", deps.Select(d => "\"" + d + "\":{}"));
        var meta = "{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\",\"dependencies\":{" + depObj + "}}";
        using (var s = zip.CreateEntry("meta.json").Open())
            s.Write(Encoding.UTF8.GetBytes(meta));
        using (var s = zip.CreateEntry("Custom/x.vam").Open())
            s.Write(Encoding.UTF8.GetBytes("x"));
    }
}
