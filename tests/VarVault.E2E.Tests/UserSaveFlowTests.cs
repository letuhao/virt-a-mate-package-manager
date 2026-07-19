using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// UserSave scan + orphan reasoning: a var referenced only by the user's own scene is NOT an orphan.
/// (Checklist 2.12/2.13.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class UserSaveFlowTests
{
    [Fact]
    public async Task Var_needed_only_by_a_user_save_is_not_an_orphan()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var savesDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        // A package with no other package depending on it.
        WriteVar(repoDir, "Creator.Dress.2.var", "Creator", "Dress");
        WriteVar(repoDir, "Lonely.Thing.1.var", "Lonely", "Thing");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        // The user's own scene references Creator.Dress.2.
        var scenePath = Path.Combine(savesDir.Path, "scene", "my.json");
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
        await File.WriteAllTextAsync(scenePath, """{ "clothing": "Creator.Dress.2:/Custom/Clothing/d.vam" }""");

        using var scope = host.Host.Services.CreateScope();
        var scanner = scope.ServiceProvider.GetRequiredService<UserSaveScanner>();
        var count = await scanner.ScanAsync(savesDir.Path);
        Assert.Equal(1, count);

        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var dress = await db.Packages.FirstAsync(p => p.VarName == "Creator.Dress.2");
        var lonely = await db.Packages.FirstAsync(p => p.VarName == "Lonely.Thing.1");

        // The SaveDependency resolved to the dress package.
        Assert.True(await db.SaveDependencies.AnyAsync(d => d.ResolvedPackageId == dress.Id));

        var references = scope.ServiceProvider.GetRequiredService<IReferenceQuery>();
        Assert.True(await references.IsReferencedAsync(dress.Id));   // needed by the user's save (2.12)
        Assert.False(await references.IsReferencedAsync(lonely.Id)); // genuinely unreferenced
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
