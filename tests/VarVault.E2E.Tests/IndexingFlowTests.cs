using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// End-to-end indexing: a composed host indexes a repository on disk into SQLite, writing
/// Package/VarFile/ContentItem/Dependency/PackageContentCount + the PackageListItem read model,
/// then re-indexes incrementally and prunes vanished files. (Checklist 1.17/1.23/1.26/1.28/1.36/BE-C5.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class IndexingFlowTests
{
    [Fact]
    public async Task Indexes_a_repository_into_the_catalog()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await RegisterRepository(host, repoDir.Path);

        WriteVar(repoDir, "Creator.PackageA.1.var",
            meta: """{"creatorName":"Creator","packageName":"PackageA","licenseType":"CC BY","dependencies":{"Other.Dep.1":{},"Qing.精绝女王.1":{}}}""",
            entries: [("Saves/scene/s.json", "{}"), ("Custom/Clothing/Female/dress.vam", "x")]);
        WriteVar(repoDir, "Creator.PackageB.2.var",
            meta: """{"creatorName":"Creator","packageName":"PackageB"}""",
            entries: [("Custom/Hair/Female/hair.vam", "y")]);

        var indexer = host.Get<IIndexingService>();
        var result = await indexer.IndexRepositoryAsync(repoId, repoDir.Path);

        Assert.Equal(2, result.Indexed);
        Assert.Equal(0, result.Skipped);

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();

        Assert.Equal(2, await db.Packages.CountAsync());
        Assert.Equal(2, await db.VarFiles.CountAsync());
        Assert.True(await db.ContentItems.AnyAsync());
        Assert.Equal(2, await db.Dependencies.CountAsync()); // Other.Dep.1 + Qing.精绝女王.1 on PackageA
        Assert.True(await db.PackageContentCounts.AnyAsync());

        // Read model materialized (1.36).
        var listA = await db.PackageListItems.FirstAsync(p => p.VarName == "Creator.PackageA.1");
        Assert.Equal(ContentType.Scene, listA.PrimaryType); // scene wins precedence
        Assert.Equal(1, listA.OnlineInstanceCount);
        Assert.True(listA.IsSingleCopy);

        // Canonical elected; content counts derived from it (BE-C5).
        var pkgA = await db.Packages.FirstAsync(p => p.VarName == "Creator.PackageA.1");
        Assert.NotNull(pkgA.CanonicalVarFileId);
    }

    [Fact]
    public async Task Reindex_is_incremental_and_prunes_vanished_files()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await RegisterRepository(host, repoDir.Path);

        WriteVar(repoDir, "A.One.1.var", """{"creatorName":"A","packageName":"One"}""", [("Custom/Hair/h.vam", "x")]);
        WriteVar(repoDir, "A.Two.1.var", """{"creatorName":"A","packageName":"Two"}""", [("Custom/Hair/h.vam", "y")]);

        var indexer = host.Get<IIndexingService>();
        var first = await indexer.IndexRepositoryAsync(repoId, repoDir.Path);
        Assert.Equal(2, first.Indexed);

        // Second scan of an unchanged repo: everything fresh, nothing re-indexed. (1.26)
        var second = await indexer.IndexRepositoryAsync(repoId, repoDir.Path);
        Assert.Equal(0, second.Indexed);
        Assert.Equal(2, second.Skipped);

        // Delete a file → prune on the next online scan. (1.28 ⚠)
        File.Delete(Path.Combine(repoDir.Path, "A.Two.1.var"));
        var third = await indexer.IndexRepositoryAsync(repoId, repoDir.Path);
        Assert.Equal(1, third.Pruned);

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        Assert.Equal(1, await db.VarFiles.CountAsync());
        Assert.False(await db.VarFiles.AnyAsync(v => v.RelativePath == "A.Two.1.var"));
    }

    [Fact]
    public async Task Offline_repository_does_not_prune_vanished_files()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await RegisterRepository(host, repoDir.Path);

        WriteVar(repoDir, "A.One.1.var", """{"creatorName":"A","packageName":"One"}""", [("Custom/Hair/h.vam", "x")]);
        var indexer = host.Get<IIndexingService>();
        await indexer.IndexRepositoryAsync(repoId, repoDir.Path);

        // Take the repo offline, then remove the file and rescan: the row must survive (offline ≠ gone).
        await SetRepositoryOnline(host, repoId, online: false);
        File.Delete(Path.Combine(repoDir.Path, "A.One.1.var"));

        var result = await indexer.IndexRepositoryAsync(repoId, repoDir.Path);
        Assert.Equal(0, result.Pruned); // ⚠ never prune an offline repo's rows

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        Assert.Equal(1, await db.VarFiles.CountAsync());
    }

    [Fact]
    public async Task Meta_divergent_creator_is_flagged()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await RegisterRepository(host, repoDir.Path);

        // Filename says Creator/PackageX; meta.json says a different author.
        WriteVar(repoDir, "Creator.PackageX.1.var",
            """{"creatorName":"SomeoneElse","packageName":"PackageX"}""", [("Custom/Hair/h.vam", "x")]);

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var pkg = await db.Packages.FirstAsync();
        Assert.True(pkg.MetaDivergent); // 1.17
        Assert.Equal("SomeoneElse", pkg.MetaCreator);
    }

    // D: 1.31 — full index of the real corpus; reports throughput and flagged files.
    [Fact]
    public async Task Indexes_the_real_repository_corpus()
    {
        const string repo = @"D:\VarVault_test_repo";
        if (!Directory.Exists(repo))
            return;

        await using var host = TestHost.Create(withPersistence: true);
        var repoId = await RegisterRepository(host, repo);

        var started = Environment.TickCount64;
        var result = await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repo);
        var elapsedMs = Environment.TickCount64 - started;

        Assert.True(result.Indexed > 50, $"expected a substantial corpus, indexed {result.Indexed}");

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var packages = await db.Packages.CountAsync();
        var varFiles = await db.VarFiles.CountAsync();
        var quarantined = await db.VarFiles.CountAsync(v => v.QuarantineKind != QuarantineKind.None);
        var listItems = await db.PackageListItems.CountAsync();

        Assert.True(packages > 0);
        Assert.Equal(varFiles - await db.VarFiles.CountAsync(v => v.PackageId == null),
            await db.VarFiles.CountAsync(v => v.PackageId != null)); // sanity
        Assert.True(listItems > 0);
        Assert.True(quarantined > 0, "the corpus has ___VarRedundant____ quarantine dirs");

        // Throughput sanity — not a hard perf gate, just proves it completes at a reasonable rate.
        Assert.True(elapsedMs < 120_000, $"indexing {result.Indexed} vars took {elapsedMs} ms");
    }

    private static async Task<Guid> RegisterRepository(TestHost host, string path)
    {
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var id = Guid.NewGuid();
        db.Repositories.Add(new Repository
        {
            Id = id, Name = "test", MountPath = path, IsOnline = true, IsEnabled = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SetRepositoryOnline(TestHost host, Guid repoId, bool online)
    {
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var repo = await db.Repositories.FirstAsync(r => r.Id == repoId);
        repo.IsOnline = online;
        await db.SaveChangesAsync();
    }

    private static void WriteVar(TempDirectory dir, string fileName, string meta, (string Name, string Content)[] entries)
    {
        var path = Path.Combine(dir.Path, fileName);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        AddEntry(zip, "meta.json", meta);
        foreach (var (name, content) in entries)
            AddEntry(zip, name, content);
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
