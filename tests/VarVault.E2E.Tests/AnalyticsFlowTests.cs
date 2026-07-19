using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>Analytics: space broken down by creator/type/tier over an indexed catalog. (Checklist 5.16.)</summary>
[Trait("Category", TestCategories.E2E)]
public sealed class AnalyticsFlowTests
{
    [Fact]
    public async Task Space_breaks_down_by_creator_type_and_tier()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "Alice.Scene.1.var", "Alice", "Scene", "Saves/scene/s.json");
        WriteVar(repoDir, "Alice.Hair.1.var", "Alice", "Hair", "Custom/Hair/h.vam");
        WriteVar(repoDir, "Bob.Clothes.1.var", "Bob", "Clothes", "Custom/Clothing/c.vam");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var analytics = scope.ServiceProvider.GetRequiredService<IAnalyticsService>();

        var byCreator = await analytics.SpaceByCreatorAsync();
        Assert.Contains(byCreator, g => g.Group == "Alice" && g.Count == 2);
        Assert.Contains(byCreator, g => g.Group == "Bob" && g.Count == 1);

        var byType = await analytics.SpaceByTypeAsync();
        Assert.Contains(byType, g => g.Group == "Scene");

        var byTier = await analytics.SpaceByTierAsync();
        Assert.Contains(byTier, g => g.Count == 3); // all three vars on the one repo's tier
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

    private static void WriteVar(TempDirectory dir, string fileName, string creator, string package, string contentEntry)
    {
        var path = Path.Combine(dir.Path, fileName);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "meta.json", "{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\"}");
        Add(zip, contentEntry, new string('x', 2000));
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
