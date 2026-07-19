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
/// Forward-dependency closure over a real indexed + resolved graph: a chain resolves transitively and
/// a cycle terminates. (Checklist 2.10.)
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
