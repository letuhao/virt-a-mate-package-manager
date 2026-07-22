using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// Forward-dependency closure over a real indexed + resolved graph: deep chains, diamonds, cycles,
/// intermediate <c>.latest</c>, and unresolved-branch reporting. (Checklist 2.10; deep dependency closure.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ForwardClosureFlowTests
{
    [Fact]
    public async Task Chain_resolves_transitively()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        // A → B → C
        WriteVar(repoDir, "X.A.1.var", "X", "A", "X.B.1");
        WriteVar(repoDir, "X.B.1.var", "X", "B", "X.C.1");
        WriteVar(repoDir, "X.C.1.var", "X", "C");

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);
        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();

        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var a = await db.Packages.FirstAsync(p => p.VarName == "X.A.1");
        var b = await db.Packages.FirstAsync(p => p.VarName == "X.B.1");
        var c = await db.Packages.FirstAsync(p => p.VarName == "X.C.1");

        var closure = await scope.ServiceProvider.GetRequiredService<IDependencyGraph>().ForwardClosureAsync(a.Id);

        Assert.Equal(2, closure.Count);
        Assert.Contains(b.Id, closure);
        Assert.Contains(c.Id, closure);
        Assert.DoesNotContain(a.Id, closure); // excludes itself
    }

    [Fact]
    public async Task Four_level_chain_returns_all_transitive_dependencies()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        // A → B → C → D
        WriteVar(repoDir, "D.A.1.var", "D", "A", "D.B.1");
        WriteVar(repoDir, "D.B.1.var", "D", "B", "D.C.1");
        WriteVar(repoDir, "D.C.1.var", "D", "C", "D.D.1");
        WriteVar(repoDir, "D.D.1.var", "D", "D");

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);
        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var a = await db.Packages.FirstAsync(p => p.VarName == "D.A.1");
        var ids = await db.Packages.Where(p => p.VarName != "D.A.1").Select(p => p.Id).ToListAsync();

        var closure = await scope.ServiceProvider.GetRequiredService<IDependencyGraph>().ForwardClosureAsync(a.Id);
        Assert.Equal(3, closure.Count);
        Assert.True(ids.All(id => closure.Contains(id)));
    }

    [Fact]
    public async Task Chain_longer_than_fifty_still_reaches_the_end()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        const int depth = 55;
        for (var i = 0; i < depth; i++)
        {
            var name = $"L.N{i}.1";
            var dep = i + 1 < depth ? $"L.N{i + 1}.1" : null;
            WriteVar(repoDir, name + ".var", "L", $"N{i}", dep is null ? [] : [dep]);
        }

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);
        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var root = await db.Packages.FirstAsync(p => p.VarName == "L.N0.1");
        var tip = await db.Packages.FirstAsync(p => p.VarName == $"L.N{depth - 1}.1");

        var closure = await scope.ServiceProvider.GetRequiredService<IDependencyGraph>().ForwardClosureAsync(root.Id);
        Assert.Equal(depth - 1, closure.Count);
        Assert.Contains(tip.Id, closure);
    }

    [Fact]
    public async Task Diamond_graph_deduplicates_shared_dependency()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        // A → B, A → C, B → D, C → D
        WriteVar(repoDir, "M.A.1.var", "M", "A", "M.B.1", "M.C.1");
        WriteVar(repoDir, "M.B.1.var", "M", "B", "M.D.1");
        WriteVar(repoDir, "M.C.1.var", "M", "C", "M.D.1");
        WriteVar(repoDir, "M.D.1.var", "M", "D");

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);
        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var a = await db.Packages.FirstAsync(p => p.VarName == "M.A.1");
        var d = await db.Packages.FirstAsync(p => p.VarName == "M.D.1");

        var closure = await scope.ServiceProvider.GetRequiredService<IDependencyGraph>().ForwardClosureAsync(a.Id);
        Assert.Equal(3, closure.Count);
        Assert.Equal(1, closure.Count(id => id == d.Id));
    }

    [Fact]
    public async Task Cycle_terminates()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        // A → B → A (cycle)
        WriteVar(repoDir, "Y.A.1.var", "Y", "A", "Y.B.1");
        WriteVar(repoDir, "Y.B.1.var", "Y", "B", "Y.A.1");

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);
        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();

        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var a = await db.Packages.FirstAsync(p => p.VarName == "Y.A.1");
        var b = await db.Packages.FirstAsync(p => p.VarName == "Y.B.1");

        var closure = await scope.ServiceProvider.GetRequiredService<IDependencyGraph>().ForwardClosureAsync(a.Id);

        // Terminates with exactly B (A excluded as the root, no infinite loop).
        Assert.Equal([b.Id], closure);
    }

    [Fact]
    public async Task Intermediate_latest_follows_highest_version_branch()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        // A → B.latest; B1 has no further deps; B2 → C → D
        WriteVar(repoDir, "Z.A.1.var", "Z", "A", "Z.B.latest");
        WriteVar(repoDir, "Z.B.1.var", "Z", "B");
        WriteVar(repoDir, "Z.B.2.var", "Z", "B", "Z.C.1");
        WriteVar(repoDir, "Z.C.1.var", "Z", "C", "Z.D.1");
        WriteVar(repoDir, "Z.D.1.var", "Z", "D");

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);
        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var a = await db.Packages.FirstAsync(p => p.VarName == "Z.A.1");
        var b2 = await db.Packages.FirstAsync(p => p.VarName == "Z.B.2");
        var c = await db.Packages.FirstAsync(p => p.VarName == "Z.C.1");
        var d = await db.Packages.FirstAsync(p => p.VarName == "Z.D.1");
        var b1 = await db.Packages.FirstAsync(p => p.VarName == "Z.B.1");

        var closure = await scope.ServiceProvider.GetRequiredService<IDependencyGraph>().ForwardClosureAsync(a.Id);
        Assert.Contains(b2.Id, closure);
        Assert.Contains(c.Id, closure);
        Assert.Contains(d.Id, closure);
        Assert.DoesNotContain(b1.Id, closure);
    }

    [Fact]
    public async Task Unresolved_edge_is_reported_and_stops_that_branch()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "U.A.1.var", "U", "A", "U.B.1");
        WriteVar(repoDir, "U.B.1.var", "U", "B", "Ghost.Missing.9");

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);
        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var a = await db.Packages.FirstAsync(p => p.VarName == "U.A.1");
        var b = await db.Packages.FirstAsync(p => p.VarName == "U.B.1");

        var detailed = await scope.ServiceProvider.GetRequiredService<IDependencyGraph>()
            .ForwardClosureDetailedAsync(a.Id);
        Assert.Equal([b.Id], detailed.PackageIds);
        Assert.Contains(detailed.UnresolvedEdges, e => e.DependsOnRefRaw == "Ghost.Missing.9");
    }

    [Fact]
    public async Task Null_canonical_is_reported_as_unresolved_edge()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        db.Packages.Add(new Package
        {
            VarName = "Orphan.NoCanonical.1",
            Creator = "Orphan",
            PackageName = "NoCanonical",
            VersionToken = "1",
            VersionSort = 1,
            IdentityKey = VarVault.Domain.Identity.IdentityFold.Compute("Orphan.NoCanonical.1"),
            FirstSeenAt = DateTime.UtcNow,
            LastIndexedAt = DateTime.UtcNow,
            CanonicalVarFileId = null,
        });
        await db.SaveChangesAsync();
        var pkg = await db.Packages.FirstAsync(p => p.VarName == "Orphan.NoCanonical.1");

        var detailed = await scope.ServiceProvider.GetRequiredService<IDependencyGraph>()
            .ForwardClosureDetailedAsync(pkg.Id);
        Assert.Empty(detailed.PackageIds);
        Assert.Contains(detailed.UnresolvedEdges, e =>
            e.FromPackageId == pkg.Id && e.DependsOnRefRaw.StartsWith("canonical-missing:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Current_latest_returns_the_highest_version()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "C.Pkg.1.var", "C", "Pkg");
        WriteVar(repoDir, "C.Pkg.3.var", "C", "Pkg");
        WriteVar(repoDir, "C.Pkg.2.var", "C", "Pkg");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var v3 = await db.Packages.FirstAsync(p => p.VarName == "C.Pkg.3");

        var latest = await scope.ServiceProvider.GetRequiredService<IDependencyGraph>().CurrentLatestAsync("C", "Pkg");
        Assert.Equal(v3.Id, latest); // highest VersionSort

        Assert.Null(await scope.ServiceProvider.GetRequiredService<IDependencyGraph>().CurrentLatestAsync("C", "Ghost"));
    }

    [Fact]
    public async Task Current_latest_matches_fold_casing()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "Creator.Pack.2.var", "Creator", "Pack");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var pkg = await db.Packages.FirstAsync(p => p.VarName == "Creator.Pack.2");
        var latest = await scope.ServiceProvider.GetRequiredService<IDependencyGraph>()
            .CurrentLatestAsync("creator", "pack");
        Assert.Equal(pkg.Id, latest);
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

    private static void WriteVar(TempDirectory dir, string fileName, string creator, string package, params string[] deps)
    {
        var path = Path.Combine(dir.Path, fileName);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var depObj = string.Join(",", deps.Select(d => "\"" + d + "\":{}"));
        var meta = "{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\",\"dependencies\":{" + depObj + "}}";
        Add(zip, "meta.json", meta);
        Add(zip, "Custom/Hair/h.vam", "x"); // give it a canonical var with content
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
