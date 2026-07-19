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
/// Durable migration state machine: a var moves from one repo to another via copy→verify→rename, the
/// target row is inserted post-verify, canonical + links re-point to it, the source goes to trash, and
/// the job reaches Done — with resume from a mid-state completing correctly. (5.7/5.12/5.13.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class MigrationFlowTests
{
    [Fact]
    public async Task Migrates_a_var_between_repos_and_trashes_the_source()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var sourceRepo = new TempDirectory();
        using var targetRepo = new TempDirectory();
        var sourceId = await Register(host, sourceRepo.Path);
        var targetId = await Register(host, targetRepo.Path);

        WriteVar(sourceRepo, "A.Thing.1.var", "A", "Thing");
        await host.Get<IIndexingService>().IndexRepositoryAsync(sourceId, sourceRepo.Path);

        long jobId, sourceVarId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            var vf = await db.VarFiles.FirstAsync();
            sourceVarId = vf.Id;
            var job = new MigrationJob { VarFileId = vf.Id, SourceRepositoryId = sourceId, TargetRepositoryId = targetId, State = MigrationState.Planned };
            db.MigrationJobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;

            var state = await scope.ServiceProvider.GetRequiredService<MigrationRunner>().RunAsync(jobId);
            Assert.Equal(MigrationState.Done, state);
        }

        // Target file exists; source moved to trash; catalog re-pointed.
        Assert.True(File.Exists(Path.Combine(targetRepo.Path, "A.Thing.1.var")));
        Assert.False(File.Exists(Path.Combine(sourceRepo.Path, "A.Thing.1.var")));

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            Assert.False(await db.VarFiles.AnyAsync(v => v.Id == sourceVarId));   // source row gone
            var target = await db.VarFiles.SingleAsync();
            Assert.Equal(targetId, target.RepositoryId);                          // target row exists
            var pkg = await db.Packages.SingleAsync();
            Assert.Equal(target.Id, pkg.CanonicalVarFileId);                      // canonical re-pointed (5.12)
        }
    }

    [Fact]
    public async Task Resume_from_copying_completes_the_migration()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var sourceRepo = new TempDirectory();
        using var targetRepo = new TempDirectory();
        var sourceId = await Register(host, sourceRepo.Path);
        var targetId = await Register(host, targetRepo.Path);

        WriteVar(sourceRepo, "A.Thing.1.var", "A", "Thing");
        await host.Get<IIndexingService>().IndexRepositoryAsync(sourceId, sourceRepo.Path);

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var vf = await db.VarFiles.FirstAsync();
        // Simulate a crash mid-run: a job persisted in Copying.
        var job = new MigrationJob { VarFileId = vf.Id, SourceRepositoryId = sourceId, TargetRepositoryId = targetId, State = MigrationState.Copying };
        db.MigrationJobs.Add(job);
        await db.SaveChangesAsync();

        var state = await scope.ServiceProvider.GetRequiredService<MigrationRunner>().RunAsync(job.Id);
        Assert.Equal(MigrationState.Done, state);
        Assert.True(File.Exists(Path.Combine(targetRepo.Path, "A.Thing.1.var")));
    }

    [Fact]
    public async Task Refuses_to_migrate_to_a_removable_target()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var sourceRepo = new TempDirectory();
        using var targetRepo = new TempDirectory();
        var sourceId = await Register(host, sourceRepo.Path);
        var targetId = await Register(host, targetRepo.Path);

        WriteVar(sourceRepo, "A.Thing.1.var", "A", "Thing");
        await host.Get<IIndexingService>().IndexRepositoryAsync(sourceId, sourceRepo.Path);

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var target = await db.Repositories.FirstAsync(r => r.Id == targetId);
        target.MediaType = MediaType.Removable; // ⚠ never migrate here (5.11)
        var vf = await db.VarFiles.FirstAsync();
        var job = new MigrationJob { VarFileId = vf.Id, SourceRepositoryId = sourceId, TargetRepositoryId = targetId, State = MigrationState.Planned };
        db.MigrationJobs.Add(job);
        await db.SaveChangesAsync();

        var state = await scope.ServiceProvider.GetRequiredService<MigrationRunner>().RunAsync(job.Id);
        Assert.Equal(MigrationState.Failed, state);
        Assert.True(File.Exists(Path.Combine(sourceRepo.Path, "A.Thing.1.var"))); // source untouched
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
