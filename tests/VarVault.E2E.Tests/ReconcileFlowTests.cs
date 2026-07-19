using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// Filesystem-truth reconcile (X.5): after a restore, repos are re-marked online/offline by whether
/// the mount exists, and vanished files in online repos are pruned — offline repos are left intact.
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ReconcileFlowTests
{
    [Fact]
    public async Task Reconcile_prunes_vanished_online_files_and_marks_offline_repos()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var onlineRepo = new TempDirectory();
        var onlineId = await Register(host, onlineRepo.Path, online: true);

        WriteVar(onlineRepo, "A.Keep.1.var", "A", "Keep");
        WriteVar(onlineRepo, "A.Gone.1.var", "A", "Gone");
        await host.Get<IIndexingService>().IndexRepositoryAsync(onlineId, onlineRepo.Path);

        // Register a second repo whose mount no longer exists (removable unplugged), with a surviving row.
        Guid offlineId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            offlineId = Guid.NewGuid();
            db.Repositories.Add(new Repository
            {
                Id = offlineId, Name = "removable", MountPath = @"Z:\gone", IsOnline = true, IsEnabled = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            db.VarFiles.Add(new VarFile { RepositoryId = offlineId, RelativePath = "offline.var" });
            await db.SaveChangesAsync();
        }

        // Delete a file from the online repo, then reconcile.
        File.Delete(Path.Combine(onlineRepo.Path, "A.Gone.1.var"));

        using (var scope = host.Host.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<CatalogReconciler>().ReconcileAsync();
            Assert.Equal(1, result.ReposOnline);
            Assert.Equal(1, result.ReposOffline);
            Assert.Equal(1, result.VarFilesPruned); // only the vanished online file
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            Assert.False(await db.Repositories.AnyAsync(r => r.Id == offlineId && r.IsOnline)); // marked offline
            Assert.True(await db.VarFiles.AnyAsync(v => v.RelativePath == "A.Keep.1.var"));
            Assert.False(await db.VarFiles.AnyAsync(v => v.RelativePath == "A.Gone.1.var"));  // pruned
            Assert.True(await db.VarFiles.AnyAsync(v => v.RelativePath == "offline.var"));    // offline row survives
        }
    }

    private static async Task<Guid> Register(TestHost host, string path, bool online)
    {
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var id = Guid.NewGuid();
        db.Repositories.Add(new Repository
        {
            Id = id, Name = "t", MountPath = path, IsOnline = online, IsEnabled = true,
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
