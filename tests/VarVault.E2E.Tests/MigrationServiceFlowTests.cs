using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// BE-N3 · Migration service runs an approved move through the durable state machine (copy → verify →
/// rename → trash source), leaving the file at the target and re-pointing the catalog. (16-checklist BE-N3.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class MigrationServiceFlowTests
{
    [Fact]
    public async Task Run_moves_the_var_to_the_target_and_trashes_the_source()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var src = new TempDirectory();
        using var dst = new TempDirectory();
        var srcId = Guid.NewGuid();
        var dstId = Guid.NewGuid();
        const string rel = "Creator.Pkg.1.var";
        await File.WriteAllTextAsync(Path.Combine(src.Path, rel), "var-bytes");

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            db.Repositories.Add(Repo(srcId, src.Path));
            db.Repositories.Add(Repo(dstId, dst.Path));
            db.Packages.Add(new Package
            {
                Id = 1, VarName = "Creator.Pkg.1", IdentityKey = "CREATOR.PKG.1", Creator = "Creator",
                PackageName = "Pkg", VersionToken = "1", VersionSort = 1,
                FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
            });
            db.VarFiles.Add(new VarFile
            {
                Id = 1, PackageId = 1, RepositoryId = srcId, RelativePath = rel,
                SizeBytes = 9, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        MigrationRunResult result;
        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IMigrationService>();
            result = await svc.RunAsync([new MigrationRequest(1, dstId)]);
        }

        Assert.Equal(1, result.Moved);
        Assert.Equal(0, result.Failed);
        Assert.True(File.Exists(Path.Combine(dst.Path, rel)), "file should exist at target");
        Assert.False(File.Exists(Path.Combine(src.Path, rel)), "source should be trashed");
    }

    // Cross-drive real move over the reserved (empty) move-target repos, if mounted. Non-destructive:
    // it moves a scratch file we create, not the corpus.
    [Theory]
    [MemberData(nameof(TestCorpus.ConfiguredRootPairs), MemberType = typeof(TestCorpus))]
    public async Task Cross_drive_move_between_reserved_repos(string srcRoot, string dstRoot)
    {
        if (string.IsNullOrEmpty(srcRoot) || string.IsNullOrEmpty(dstRoot))
            return; // set VARVAULT_TEST_CORPUS and _2 (different drives) to run this cross-drive test

        var scratchDir = Path.Combine(srcRoot, "__vv_move_test__");
        Directory.CreateDirectory(scratchDir);
        var rel = Path.Combine("__vv_move_test__", "Scratch.Move.1.var");
        var srcFile = Path.Combine(srcRoot, rel);
        await File.WriteAllTextAsync(srcFile, "scratch");
        var dstFile = Path.Combine(dstRoot, rel);

        try
        {
            await using var host = TestHost.Create(withPersistence: true);
            var srcId = Guid.NewGuid();
            var dstId = Guid.NewGuid();
            using (var scope = host.Host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
                db.Repositories.Add(Repo(srcId, srcRoot));
                db.Repositories.Add(Repo(dstId, dstRoot));
                db.Packages.Add(new Package
                {
                    Id = 1, VarName = "Scratch.Move.1", IdentityKey = "SCRATCH.MOVE.1", Creator = "Scratch",
                    PackageName = "Move", VersionToken = "1", VersionSort = 1,
                    FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
                });
                db.VarFiles.Add(new VarFile
                {
                    Id = 1, PackageId = 1, RepositoryId = srcId, RelativePath = rel,
                    SizeBytes = 7, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            using var runScope = host.Host.Services.CreateScope();
            var result = await runScope.ServiceProvider.GetRequiredService<IMigrationService>()
                .RunAsync([new MigrationRequest(1, dstId)]);

            Assert.Equal(1, result.Moved);
            Assert.True(File.Exists(dstFile));
            Assert.False(File.Exists(srcFile)); // trashed from source drive
        }
        finally
        {
            try { if (Directory.Exists(scratchDir)) Directory.Delete(scratchDir, recursive: true); } catch { }
            try { if (File.Exists(dstFile)) File.Delete(dstFile); } catch { }
            try { Directory.Delete(Path.Combine(dstRoot, "__vv_move_test__"), true); } catch { }
        }
    }

    private static Repository Repo(Guid id, string path) => new()
    {
        Id = id, Name = "r", MountPath = path, Tier = 3, MediaType = MediaType.Hdd,
        IsOnline = true, IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };
}
