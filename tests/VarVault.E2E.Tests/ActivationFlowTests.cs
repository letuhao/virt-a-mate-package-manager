using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Presets;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// Activation link building: a preset's members + dependency closure become ActivationLink rows keyed
/// by the hottest online copy, with Explicit vs DependencyOf reasons. (Checklist 3.4/3.5/3.7.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ActivationFlowTests
{
    [Fact]
    public async Task Builds_links_for_members_and_their_closure()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "A.Look.1.var", "A", "Look", "A.Base.1"); // member, depends on Base
        WriteVar(repoDir, "A.Base.1.var", "A", "Base");             // pulled in as a dependency
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();

        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>()
            .CreateAsync("P", ["A.Look.1"])).Value;

        var result = await scope.ServiceProvider.GetRequiredService<IActivationService>().BuildProfileLinksAsync(preset.Id);
        Assert.Equal(2, result.LinksCreated); // Look + Base
        Assert.Equal(0, result.MissingPackages);

        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var look = await db.Packages.FirstAsync(p => p.VarName == "A.Look.1");
        var baseP = await db.Packages.FirstAsync(p => p.VarName == "A.Base.1");

        var links = await db.ActivationLinks.ToListAsync();
        Assert.Equal(2, links.Count);
        // Each link is keyed by a VarFile, not a package, and carries a reason. (3.4/3.5)
        var lookVarId = (await db.VarFiles.FirstAsync(v => v.PackageId == look.Id)).Id;
        var baseVarId = (await db.VarFiles.FirstAsync(v => v.PackageId == baseP.Id)).Id;
        Assert.Contains(links, l => l.VarFileId == lookVarId && l.Reason == ActivationReason.Explicit);
        Assert.Contains(links, l => l.VarFileId == baseVarId && l.Reason == ActivationReason.DependencyOf);
        Assert.All(links, l => Assert.Equal(LinkKind.Install, l.LinkKind));
    }

    [Fact]
    public async Task Rebuild_replaces_prior_links()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "A.Solo.1.var", "A", "Solo");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>().CreateAsync("P", ["A.Solo.1"])).Value;
        var activation = scope.ServiceProvider.GetRequiredService<IActivationService>();

        await activation.BuildProfileLinksAsync(preset.Id);
        await activation.BuildProfileLinksAsync(preset.Id); // rebuild

        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        Assert.Equal(1, await db.ActivationLinks.CountAsync()); // not duplicated
    }

    private static async Task<Guid> Register(TestHost host, string path)
    {
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var id = Guid.NewGuid();
        db.Repositories.Add(new Repository
        {
            Id = id, Name = "t", MountPath = path, IsOnline = true, IsEnabled = true, Tier = 1,
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
        Add(zip, "meta.json", "{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\",\"dependencies\":{" + depObj + "}}");
        Add(zip, "Custom/Hair/h.vam", "x");
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
