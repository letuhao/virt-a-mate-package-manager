using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Startup X.5 · AppHost.ReconcileCatalog prunes vanished-on-disk vars before index.</summary>
[Trait("Category", TestCategories.E2E)]
public sealed class StartupReconcileTests
{
    [Fact]
    public async Task ReconcileCatalog_prunes_file_deleted_from_disk()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();

        Guid repoId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            repoId = Guid.NewGuid();
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "hot", MountPath = repoDir.Path, IsOnline = true, IsEnabled = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        WriteVar(Path.Combine(repoDir.Path, "Keep.Me.1.var"), "Keep", "Me");
        WriteVar(Path.Combine(repoDir.Path, "Gone.Me.1.var"), "Gone", "Me");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        File.Delete(Path.Combine(repoDir.Path, "Gone.Me.1.var"));

        var result = AppHost.ReconcileCatalog(host.Host.Services);
        Assert.NotNull(result);
        Assert.True(result!.VarFilesPruned >= 1);

        using var check = host.Host.Services.CreateScope();
        var catalog = check.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        Assert.True(await catalog.VarFiles.AnyAsync(v => v.RelativePath == "Keep.Me.1.var"));
        Assert.False(await catalog.VarFiles.AnyAsync(v => v.RelativePath == "Gone.Me.1.var"));
    }

    private static void WriteVar(string path, string creator, string package)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("meta.json", CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes($"{{\"creatorName\":\"{creator}\",\"packageName\":\"{package}\"}}"));
    }
}
