using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// Repository-signature fast-path: skip unchanged repos on auto-index, detect changes, force full. (A16.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class RepositorySignatureFastPathTests
{
    [Fact]
    public async Task Second_auto_index_skips_unchanged_repository()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        WriteMinimalVar(Path.Combine(repoDir.Path, "A.One.1.var"));

        var repoId = await SeedRepoAsync(host, repoDir.Path);

        using (var scope = host.Host.Services.CreateScope())
        {
            var indexer = scope.ServiceProvider.GetRequiredService<IStreamIndexer>();
            var first = await indexer.IndexRepositoryAsync(repoId, repoDir.Path, MediaType.Ssd);
            Assert.True(first.Indexed >= 1);
        }

        var scanCountAfterFirst = await CountScanRunsAsync(host, repoId);

        using (var scope = host.Host.Services.CreateScope())
        {
            var indexer = scope.ServiceProvider.GetRequiredService<IStreamIndexer>();
            var second = await indexer.IndexRepositoryAsync(repoId, repoDir.Path, MediaType.Ssd);
            Assert.Equal(0, second.Indexed);
            Assert.Equal(1, second.Skipped);
        }

        var scanCountAfterSecond = await CountScanRunsAsync(host, repoId);
        Assert.Equal(scanCountAfterFirst, scanCountAfterSecond);
    }

    [Fact]
    public async Task Added_file_triggers_full_rescan()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        WriteMinimalVar(Path.Combine(repoDir.Path, "A.One.1.var"));

        var repoId = await SeedRepoAsync(host, repoDir.Path);

        using (var scope = host.Host.Services.CreateScope())
        {
            var indexer = scope.ServiceProvider.GetRequiredService<IStreamIndexer>();
            await indexer.IndexRepositoryAsync(repoId, repoDir.Path, MediaType.Ssd);
        }

        WriteMinimalVar(Path.Combine(repoDir.Path, "B.Two.1.var"));

        using (var scope = host.Host.Services.CreateScope())
        {
            var indexer = scope.ServiceProvider.GetRequiredService<IStreamIndexer>();
            var outcome = await indexer.IndexRepositoryAsync(repoId, repoDir.Path, MediaType.Ssd);
            Assert.True(outcome.Indexed >= 1);
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            Assert.Equal(2, await db.VarFiles.CountAsync());
        }
    }

    [Fact]
    public async Task Force_full_bypasses_unchanged_fast_path()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        WriteMinimalVar(Path.Combine(repoDir.Path, "A.One.1.var"));

        var repoId = await SeedRepoAsync(host, repoDir.Path);

        using (var scope = host.Host.Services.CreateScope())
        {
            var indexer = scope.ServiceProvider.GetRequiredService<IStreamIndexer>();
            await indexer.IndexRepositoryAsync(repoId, repoDir.Path, MediaType.Ssd);
        }

        var scanCountAfterFirst = await CountScanRunsAsync(host, repoId);

        using (var scope = host.Host.Services.CreateScope())
        {
            var indexer = scope.ServiceProvider.GetRequiredService<IStreamIndexer>();
            var forced = await indexer.IndexRepositoryAsync(
                repoId, repoDir.Path, MediaType.Ssd, forceFull: true);
            Assert.Equal(0, forced.Indexed);
            Assert.Equal(1, forced.Skipped);
        }

        var scanCountAfterForce = await CountScanRunsAsync(host, repoId);
        Assert.Equal(scanCountAfterFirst + 1, scanCountAfterForce);
    }

    [Fact]
    public async Task Completed_scan_stores_signature_on_ScanRun()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        WriteMinimalVar(Path.Combine(repoDir.Path, "A.One.1.var"));

        var repoId = await SeedRepoAsync(host, repoDir.Path);

        using (var scope = host.Host.Services.CreateScope())
        {
            var indexer = scope.ServiceProvider.GetRequiredService<IStreamIndexer>();
            await indexer.IndexRepositoryAsync(repoId, repoDir.Path, MediaType.Ssd);
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var ledger = scope.ServiceProvider.GetRequiredService<IScanLedger>();
            var sig = await ledger.GetLastCompletedSignatureAsync(repoId);
            Assert.NotNull(sig);
            Assert.Equal(1, sig.Value.FileCount);
            Assert.True(sig.Value.TotalBytes > 0);
        }
    }

    private static async Task<Guid> SeedRepoAsync(TestHost host, string mountPath)
    {
        var repoId = Guid.NewGuid();
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        db.Repositories.Add(new Repository
        {
            Id = repoId,
            Name = "t",
            MountPath = mountPath,
            IsOnline = true,
            IsEnabled = true,
            MediaType = MediaType.Ssd,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return repoId;
    }

    private static async Task<int> CountScanRunsAsync(TestHost host, Guid repoId)
    {
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        return await db.ScanRuns.CountAsync(r => r.RepositoryId == repoId);
    }

    private static void WriteMinimalVar(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("meta.json");
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes("""{"creatorName":"A","packageName":"One","dependencies":[]}"""));
    }
}
