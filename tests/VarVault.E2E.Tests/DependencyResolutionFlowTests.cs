using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// End-to-end dependency resolution over an indexed catalog: exact/latest/closest resolution,
/// missing detection, SELF handling, reverse-dependent counts and foundational flag, and the
/// materialized HasMissingDeps bit. (Checklist 2.2/2.5/2.8/2.11/2.15.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class DependencyResolutionFlowTests
{
    [Fact]
    public async Task Resolves_present_deps_and_flags_missing()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        // Base package (v1 and v2 present), a consumer depending on Base.latest + a missing package.
        WriteVar(repoDir, "Base.Thing.1.var", Meta("Base", "Thing"), [("Custom/Hair/h.vam", "a")]);
        WriteVar(repoDir, "Base.Thing.2.var", Meta("Base", "Thing"), [("Custom/Hair/h.vam", "b")]);
        WriteVar(repoDir, "Author.Consumer.1.var",
            Meta("Author", "Consumer", "Base.Thing.latest", "Ghost.Missing.1"),
            [("Custom/Clothing/c.vam", "c")]);

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDependencyResolver>();
        var result = await resolver.ResolveAllAsync();

        Assert.Equal(1, result.Missing); // Ghost.Missing.1

        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();

        // Base.Thing.latest resolves to v2 (highest).
        var baseV2 = await db.Packages.FirstAsync(p => p.VarName == "Base.Thing.2");
        var latestDep = await db.Dependencies.FirstAsync(d => d.DependsOnRefRaw == "Base.Thing.latest");
        Assert.False(latestDep.IsMissing);
        Assert.Equal(baseV2.Id, latestDep.ResolvedPackageId);
        Assert.Equal(ResolvedVia.Latest, latestDep.ResolvedVia);

        var ghost = await db.Dependencies.FirstAsync(d => d.DependsOnRefRaw == "Ghost.Missing.1");
        Assert.True(ghost.IsMissing);
        Assert.Null(ghost.ResolvedPackageId);

        // Reverse count: Base.Thing family is depended on by 1 package (Consumer).
        Assert.Equal(1, baseV2.ReverseDependentCount);

        // HasMissingDeps materialized on the consumer's read-model row.
        var consumerItem = await db.PackageListItems.FirstAsync(p => p.VarName == "Author.Consumer.1");
        Assert.True(consumerItem.HasMissingDeps);
        var baseItem = await db.PackageListItems.FirstAsync(p => p.VarName == "Base.Thing.2");
        Assert.False(baseItem.HasMissingDeps);
    }

    [Fact]
    public async Task Closest_version_substitution_is_recorded()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "Base.Thing.5.var", Meta("Base", "Thing"), [("Custom/Hair/h.vam", "a")]);
        WriteVar(repoDir, "Author.Consumer.1.var", Meta("Author", "Consumer", "Base.Thing.3"), [("Custom/Clothing/c.vam", "c")]);

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();

        var dep = await db.Dependencies.FirstAsync(d => d.DependsOnRefRaw == "Base.Thing.3");
        Assert.False(dep.IsMissing);
        Assert.True(dep.IsVersionSubstituted); // v3 requested, only v5 available
        Assert.Equal(ResolvedVia.Closest, dep.ResolvedVia);
    }

    [Fact]
    public async Task Foundational_flag_set_when_many_packages_depend_on_one()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "Core.Lib.1.var", Meta("Core", "Lib"), [("Custom/Scripts/x.cs", "a")]);
        for (var i = 0; i < EfDependencyResolver.FoundationalThreshold; i++)
            WriteVar(repoDir, $"Author.Dep{i}.1.var", Meta("Author", $"Dep{i}", "Core.Lib.1"), [("Custom/Clothing/c.vam", "c")]);

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        Assert.True(result.Foundational >= 1);

        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var core = await db.Packages.FirstAsync(p => p.VarName == "Core.Lib.1");
        Assert.True(core.IsFoundational);
        Assert.Equal(EfDependencyResolver.FoundationalThreshold, core.ReverseDependentCount);
    }

    [Fact]
    public async Task Alias_resolves_a_missing_ref_but_a_real_match_outranks_it()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        // Target package present; a consumer depends on a DIFFERENT (missing) name and on the real one.
        WriteVar(repoDir, "Real.Target.1.var", Meta("Real", "Target"), [("Custom/Hair/h.vam", "a")]);
        WriteVar(repoDir, "Author.Consumer.1.var",
            Meta("Author", "Consumer", "Renamed.Old.1", "Real.Target.1"),
            [("Custom/Clothing/c.vam", "c")]);

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var target = await db.Packages.FirstAsync(p => p.VarName == "Real.Target.1");

        // Alias: the missing "Renamed.Old.1" actually means the present target.
        db.VarAliases.Add(new VarAlias
        {
            MissingRefKey = Domain.Identity.IdentityFold.Compute("Renamed.Old.1"),
            MissingRefRaw = "Renamed.Old.1",
            ResolvedPackageId = target.Id,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();

        // The aliased missing ref now resolves via the alias.
        var aliased = await db.Dependencies.FirstAsync(d => d.DependsOnRefRaw == "Renamed.Old.1");
        Assert.False(aliased.IsMissing);
        Assert.Equal(target.Id, aliased.ResolvedPackageId);
        Assert.Equal(ResolvedVia.Alias, aliased.ResolvedVia);

        // The real match resolves directly (never via alias).
        var real = await db.Dependencies.FirstAsync(d => d.DependsOnRefRaw == "Real.Target.1");
        Assert.Equal(ResolvedVia.Exact, real.ResolvedVia);
    }

    private static string Meta(string creator, string package, params string[] deps)
    {
        var depObj = string.Join(",", deps.Select(d => "\"" + d + "\":{}"));
        return "{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package +
               "\",\"dependencies\":{" + depObj + "}}";
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

    private static void WriteVar(TempDirectory dir, string fileName, string meta, (string Name, string Content)[] entries)
    {
        var path = Path.Combine(dir.Path, fileName);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "meta.json", meta);
        foreach (var (name, content) in entries)
            Add(zip, name, content);
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
