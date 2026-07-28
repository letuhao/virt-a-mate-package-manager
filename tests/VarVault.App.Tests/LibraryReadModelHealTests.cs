using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// Exact can see Package+VarFile while Library only reads PackageListItem — a lost dirty refresh
/// left imports invisible. Startup reconcile + peek/ack dirty heal that gap.
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class LibraryReadModelHealTests
{
    [Fact]
    public async Task ReconcileCatalog_materializes_missing_PackageListItem()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();

        Guid repoId;
        long packageId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            repoId = Guid.NewGuid();
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "hot", MountPath = repoDir.Path, IsOnline = true, IsEnabled = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            packageId = 1;
            db.Packages.Add(new Package
            {
                Id = packageId, VarName = "Ghost.Pack.1", IdentityKey = "ghost.pack.1",
                Creator = "Ghost", PackageName = "Pack", VersionToken = "1", VersionSort = 1,
                FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
            });
            var path = Path.Combine(repoDir.Path, "Ghost.Pack.1.var");
            WriteVar(path, "Ghost", "Pack");
            db.VarFiles.Add(new VarFile
            {
                Id = 1, PackageId = packageId, RepositoryId = repoId,
                RelativePath = "Ghost.Pack.1.var", SizeBytes = new FileInfo(path).Length,
                FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
                ContentSignature = "sig-ghost", IngestState = IngestState.RawStored,
            });
            await db.SaveChangesAsync();

            var pkg = await db.Packages.SingleAsync(p => p.Id == packageId);
            pkg.CanonicalVarFileId = 1;
            await db.SaveChangesAsync();
        }

        // Exact would see this; Library must not — no PackageListItem yet.
        using (var check = host.Host.Services.CreateScope())
        {
            var db = check.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            Assert.True(await db.VarFiles.AnyAsync(v => v.ContentSignature == "sig-ghost"));
            Assert.False(await db.PackageListItems.AnyAsync(i => i.PackageId == packageId));

            var library = check.ServiceProvider.GetRequiredService<ILibraryQueryService>();
            var page = await library.GetPageAsync(new LibraryQuery(0, 50));
            Assert.Equal(0, page.TotalCount);
        }

        var result = AppHost.ReconcileCatalog(host.Host.Services);
        Assert.NotNull(result);
        Assert.True(result!.ListRowsHealed >= 1);

        using (var after = host.Host.Services.CreateScope())
        {
            var db = after.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            Assert.True(await db.PackageListItems.AnyAsync(i => i.PackageId == packageId));

            var library = after.ServiceProvider.GetRequiredService<ILibraryQueryService>();
            var page = await library.GetPageAsync(new LibraryQuery(0, 50));
            Assert.True(page.TotalCount >= 1);
            Assert.Contains(page.Items, i =>
                i.VarName.Contains("Ghost", StringComparison.OrdinalIgnoreCase)
                || i.Creator.Contains("Ghost", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Peek_then_ack_keeps_dirty_until_refresh_succeeds()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var dirty = scope.ServiceProvider.GetRequiredService<IDurableDirtySet>();

        await dirty.MarkAsync(99, "test");
        var peeked = await dirty.PeekBatchAsync(10);
        Assert.Contains(99L, peeked);

        // Still there until acknowledged — crash-safe vs old Drain-before-refresh.
        Assert.Contains(99L, await dirty.PeekBatchAsync(10));

        await dirty.AcknowledgeAsync(peeked);
        Assert.Empty(await dirty.PeekBatchAsync(10));
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
