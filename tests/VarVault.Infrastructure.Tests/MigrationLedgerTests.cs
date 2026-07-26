using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Integration)]
public sealed class MigrationLedgerTests
{
    [Fact]
    public async Task Migrate_fails_cleanly_when_MinFree_would_be_breached()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var src = new TempDirectory();
        using var dst = new TempDirectory();
        var srcId = Guid.NewGuid();
        var dstId = Guid.NewGuid();
        const string rel = "Creator.Pkg.1.var";
        var payload = new string('x', 10_000);
        await File.WriteAllTextAsync(Path.Combine(src.Path, rel), payload);

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            db.Repositories.Add(new Repository
            {
                Id = srcId, Name = "src", MountPath = src.Path, Tier = 3, MediaType = MediaType.Hdd,
                IsOnline = true, IsEnabled = true, FreeBytes = 100_000, MinFreeBytes = 0,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            // Destination reports tiny free space with a huge MinFree → reserve must fail.
            db.Repositories.Add(new Repository
            {
                Id = dstId, Name = "dst", MountPath = dst.Path, Tier = 1, MediaType = MediaType.Nvme,
                IsOnline = true, IsEnabled = true, FreeBytes = 5_000, MinFreeBytes = 4_000,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            db.Packages.Add(new Package
            {
                Id = 1, VarName = "Creator.Pkg.1", IdentityKey = "CREATOR.PKG.1", Creator = "Creator",
                PackageName = "Pkg", VersionToken = "1", VersionSort = 1,
                FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
            });
            db.VarFiles.Add(new VarFile
            {
                Id = 1, PackageId = 1, RepositoryId = srcId, RelativePath = rel,
                SizeBytes = payload.Length, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IMigrationService>()
                .RunAsync([new MigrationRequest(1, dstId)]);
            Assert.Equal(0, result.Moved);
            Assert.Equal(1, result.Failed);
        }

        Assert.True(File.Exists(Path.Combine(src.Path, rel)));
        Assert.False(File.Exists(Path.Combine(dst.Path, rel)));
    }

    [Fact]
    public async Task Sequential_migrates_honor_catalog_FreeBytes_MinFree()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var src = new TempDirectory();
        using var dst = new TempDirectory();
        var srcId = Guid.NewGuid();
        var dstId = Guid.NewGuid();
        // Catalog free 15k, MinFree 10k → one 4k file fits, two do not (after FreeBytes is decremented).
        const long free = 15_000;
        const long minFree = 10_000;
        const int size = 4_000;

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            db.Repositories.Add(new Repository
            {
                Id = srcId, Name = "src", MountPath = src.Path, Tier = 3, MediaType = MediaType.Hdd,
                IsOnline = true, IsEnabled = true, FreeBytes = 100_000, MinFreeBytes = 0,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            db.Repositories.Add(new Repository
            {
                Id = dstId, Name = "dst", MountPath = dst.Path, Tier = 1, MediaType = MediaType.Nvme,
                IsOnline = true, IsEnabled = true, FreeBytes = free, MinFreeBytes = minFree,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            for (var i = 1; i <= 2; i++)
            {
                var rel = $"Creator.Pkg.{i}.var";
                await File.WriteAllTextAsync(Path.Combine(src.Path, rel), new string('x', size));
                db.Packages.Add(new Package
                {
                    Id = i, VarName = $"Creator.Pkg.{i}", IdentityKey = $"CREATOR.PKG.{i}", Creator = "Creator",
                    PackageName = "Pkg", VersionToken = i.ToString(), VersionSort = i,
                    FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
                });
                db.VarFiles.Add(new VarFile
                {
                    Id = i, PackageId = i, RepositoryId = srcId, RelativePath = rel,
                    SizeBytes = size, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync();
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IMigrationService>();
            var first = await svc.RunAsync([new MigrationRequest(1, dstId)]);
            Assert.Equal(1, first.Moved);
            var second = await svc.RunAsync([new MigrationRequest(2, dstId)]);
            Assert.Equal(0, second.Moved);
            Assert.Equal(1, second.Failed);
        }

        Assert.True(File.Exists(Path.Combine(dst.Path, "Creator.Pkg.1.var")));
        Assert.True(File.Exists(Path.Combine(src.Path, "Creator.Pkg.2.var")));
        Assert.False(File.Exists(Path.Combine(dst.Path, "Creator.Pkg.2.var")));
    }
}
