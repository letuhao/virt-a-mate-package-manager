using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexer;
using VarVault.Sdk.Indexing;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// Scale-architecture gates: durable dirty set, lease reclaim, stream ingest, one-handle inspector,
/// coalesced indexer worker. (5 TB redesign A12–A15.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ScalableIndexingArchitectureTests
{
    [Fact]
    public async Task Durable_dirty_set_survives_mark_and_drain()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var dirty = scope.ServiceProvider.GetRequiredService<IDurableDirtySet>();
        await dirty.MarkAsync(42, "test");
        await dirty.MarkAsync(43, "test");
        var batch = await dirty.DrainBatchAsync(10);
        Assert.Equal(2, batch.Count);
        Assert.Empty(await dirty.DrainBatchAsync(10));
    }

    [Fact]
    public async Task Scan_ledger_claims_and_marks_raw_stored()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<IScanLedger>();
        var repoId = Guid.NewGuid();
        db.Repositories.Add(new Repository
        {
            Id = repoId, Name = "r", MountPath = "C:\\x", IsOnline = true, IsEnabled = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var run = await ledger.BeginRunAsync(repoId);
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "A.Pkg.1.var");
        WriteMinimalVar(path);
        var scanned = new ScannedVar(path, "A.Pkg.1.var", new FileInfo(path).Length, DateTime.UtcNow, QuarantineKind.None);
        await ledger.UpsertDiscoveryAsync(run.Id, repoId, scanned);

        var owner = Guid.NewGuid();
        var claimed = await ledger.ClaimWorkAsync(run.Id, repoId, owner, 10);
        Assert.Single(claimed);
        await ledger.MarkRawStoredAsync(claimed[0]);
        var vf = await db.VarFiles.AsNoTracking().SingleAsync();
        Assert.Equal(IngestState.RawStored, vf.IngestState);
    }

    [Fact]
    public async Task Stream_indexer_indexes_a_var_and_sets_raw_stored()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        WriteMinimalVar(Path.Combine(repoDir.Path, "A.One.1.var"));

        Guid repoId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            repoId = Guid.NewGuid();
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "t", MountPath = repoDir.Path, IsOnline = true, IsEnabled = true,
                MediaType = MediaType.Ssd,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var indexer = scope.ServiceProvider.GetRequiredService<IStreamIndexer>();
            var outcome = await indexer.IndexRepositoryAsync(repoId, repoDir.Path, MediaType.Ssd);
            Assert.True(outcome.Indexed >= 1);
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            var vf = await db.VarFiles.SingleAsync();
            Assert.Equal(IngestState.RawStored, vf.IngestState);
            Assert.NotNull(vf.ContentSignature);
            Assert.True(await db.PackageListItems.AnyAsync());
        }
    }

    [Fact]
    public async Task Claim_work_does_not_requeue_failed_in_same_run()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<IScanLedger>();
        var repoId = Guid.NewGuid();
        db.Repositories.Add(new Repository
        {
            Id = repoId, Name = "r", MountPath = "C:\\x", IsOnline = true, IsEnabled = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var run = await ledger.BeginRunAsync(repoId);
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "A.Bad.1.var");
        WriteMinimalVar(path);
        var scanned = new ScannedVar(path, "A.Bad.1.var", new FileInfo(path).Length, DateTime.UtcNow, QuarantineKind.None);
        await ledger.UpsertDiscoveryAsync(run.Id, repoId, scanned);

        var owner = Guid.NewGuid();
        var claimed = await ledger.ClaimWorkAsync(run.Id, repoId, owner, 10);
        Assert.Single(claimed);
        await ledger.MarkFailedAsync(claimed[0], "boom");

        // Same run must not reclaim Failed — that caused an infinite producer loop.
        Assert.Empty(await ledger.ClaimWorkAsync(run.Id, repoId, owner, 10));
    }


    [Fact]
    public void One_handle_inspector_extracts_meta_without_unbounded_read()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "A.Scene.1.var");
        WriteVarWithPreview(path);
        var inspector = new Infrastructure.Indexing.OneHandleVarInspector(new Infrastructure.Indexing.PreviewExtractor());
        var result = inspector.Inspect(path);
        Assert.NotNull(result.Inspection.Meta);
        Assert.NotNull(result.Inspection.Signatures);
        Assert.NotNull(result.RepresentativeThumbJpeg);
    }

    [Fact]
    public async Task Orchestrator_uses_stream_path_end_to_end()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        WriteMinimalVar(Path.Combine(repoDir.Path, "B.Two.1.var"));
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            db.Repositories.Add(new Repository
            {
                Id = Guid.NewGuid(), Name = "o", MountPath = repoDir.Path, IsOnline = true, IsEnabled = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var summary = await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();
            Assert.True(summary.Indexed >= 1);
        }
    }

    [Fact]
    public void Writer_lease_is_exclusive_across_acquirers()
    {
        using var dir = new TempDirectory();
        var db = Path.Combine(dir.Path, "catalog.db");

        using var first = Infrastructure.Indexing.WriterLease.TryAcquire(db);
        Assert.NotNull(first);

        // A second acquirer in the same process must fail while the first holds the file.
        var second = Infrastructure.Indexing.WriterLease.TryAcquire(db);
        Assert.Null(second);

        first!.Dispose();

        // Released → acquirable again.
        using var third = Infrastructure.Indexing.WriterLease.TryAcquire(db);
        Assert.NotNull(third);
    }

    [Fact]
    public async Task Global_write_lock_serializes_two_holders()
    {
        using var dir = new TempDirectory();
        var lockPath = Infrastructure.Threading.FileGlobalWriteLock.PathFor(Path.Combine(dir.Path, "catalog.db"));
        var lockA = new Infrastructure.Threading.FileGlobalWriteLock(lockPath);
        var lockB = new Infrastructure.Threading.FileGlobalWriteLock(lockPath);

        var scopeA = await lockA.AcquireAsync();

        // A second acquire must not complete while A holds the lock.
        var pending = lockB.AcquireAsync();
        var winner = await Task.WhenAny(pending, Task.Delay(300));
        Assert.NotSame(pending, winner); // still blocked

        await scopeA.DisposeAsync();

        // Released → B acquires promptly.
        var scopeB = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(scopeB);
        await scopeB.DisposeAsync();
    }

    [Fact]
    public async Task Null_write_lock_is_a_noop()
    {
        var scope = await new Infrastructure.Threading.NullWriteLock().AcquireAsync();
        await scope.DisposeAsync(); // must not throw
    }

    [Fact]
    public async Task Worker_tracks_owner_and_reports_liveness()
    {
        await using var host = TestHost.Create(withPersistence: true);
        var worker = host.Host.Services.GetRequiredService<IIndexerWorker>();

        Assert.False(worker.HasLiveOwner);

        // Register this (live) process as an owner.
        await worker.HandleAsync(new IndexerCommand(
            IndexerCommandKind.RegisterOwner, OwnerProcessId: Environment.ProcessId));
        Assert.True(worker.HasLiveOwner);

        // A dead pid is pruned on the next liveness read.
        await worker.HandleAsync(new IndexerCommand(
            IndexerCommandKind.UnregisterOwner, OwnerProcessId: Environment.ProcessId));
        await worker.HandleAsync(new IndexerCommand(
            IndexerCommandKind.RegisterOwner, OwnerProcessId: int.MaxValue));
        Assert.False(worker.HasLiveOwner);
    }

    private static void WriteMinimalVar(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("meta.json");
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes("""{"creatorName":"A","packageName":"One","dependencies":[]}"""));
    }

    private static void WriteVarWithPreview(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "meta.json", """{"creatorName":"A","packageName":"Scene"}""");
        Add(zip, "Saves/scene/s.json", "{}");
        // Minimal JPEG-ish bytes — Downscale returns original if undecodable.
        AddBytes(zip, "Saves/scene/s.jpg", [0xFF, 0xD8, 0xFF, 0xD9]);
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }

    private static void AddBytes(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name);
        using var s = entry.Open();
        s.Write(bytes);
    }
}
