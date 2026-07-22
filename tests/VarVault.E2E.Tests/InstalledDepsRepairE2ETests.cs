using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// Installed Packages / MissingDepends repair: deps of active packages → resolve → activate found → leftovers.
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class InstalledDepsRepairE2ETests
{
    [Fact]
    public async Task Analyze_empty_active_set_is_noop()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var repair = scope.ServiceProvider.GetRequiredService<IInstalledDepsRepair>();
        var analysis = await repair.AnalyzeAsync();
        Assert.Equal(0, analysis.ActivePackageCount);
        Assert.Empty(analysis.Entries);
    }

    [Fact]
    public async Task Analyze_active_finds_present_missing_and_closest()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        // Active Look depends on Base (present), Ghost (missing), and Pack.2 (only .1 present → closest).
        WriteVar(repoDir, "A.Look.1.var", "A", "Look", "A.Base.1", "Ghost.Missing.9", "A.Pack.2");
        WriteVar(repoDir, "A.Base.1.var", "A", "Base");
        WriteVar(repoDir, "A.Pack.1.var", "A", "Pack");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();

        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var look = await db.Packages.FirstAsync(p => p.VarName == "A.Look.1");
        var listItem = await db.PackageListItems.FirstAsync(i => i.PackageId == look.Id);
        listItem.IsActive = true;
        await db.SaveChangesAsync();

        var repair = scope.ServiceProvider.GetRequiredService<IInstalledDepsRepair>();
        var analysis = await repair.AnalyzeAsync();

        Assert.Equal(1, analysis.ActivePackageCount);
        Assert.Equal(3, analysis.Parsed);

        var bas = analysis.Entries.Single(e => e.Ref == "A.Base.1");
        Assert.True(bas.InLibrary);
        Assert.Equal("A.Base.1", bas.ResolvedVarName);
        Assert.Equal(InstalledDepsResolveVia.Exact, bas.Via);
        Assert.False(bas.NeedsAlias);

        var ghost = analysis.Entries.Single(e => e.Ref == "Ghost.Missing.9");
        Assert.False(ghost.InLibrary);
        Assert.True(ghost.NeedsAlias);

        var closest = analysis.Entries.Single(e => e.Ref == "A.Pack.2");
        Assert.True(closest.InLibrary);
        Assert.Equal("A.Pack.1", closest.ResolvedVarName);
        Assert.Equal(InstalledDepsResolveVia.Closest, closest.Via);
        Assert.True(closest.NeedsAlias);

        Assert.Equal(2, analysis.Leftovers.Count); // ghost + closest
    }

    [Fact]
    public async Task ActivateFound_adds_resolved_deps_to_preset()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "A.Look.1.var", "A", "Look", "A.Base.1");
        WriteVar(repoDir, "A.Base.1.var", "A", "Base");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();

        var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();
        var existing = await presets.CreateAsync("InstalledSet", ["A.Look.1"]);
        Assert.True(existing.IsSuccess);

        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var look = await db.Packages.FirstAsync(p => p.VarName == "A.Look.1");
        var listItem = await db.PackageListItems.FirstAsync(i => i.PackageId == look.Id);
        listItem.IsActive = true;
        await db.SaveChangesAsync();

        var repair = scope.ServiceProvider.GetRequiredService<IInstalledDepsRepair>();
        var analysis = await repair.AnalyzeAsync();
        var names = analysis.Entries
            .Where(e => e.InLibrary && e.ResolvedVarName is not null)
            .Select(e => e.ResolvedVarName!)
            .ToList();
        Assert.Contains("A.Base.1", names);

        var act = await repair.ActivateFoundAsync(names);
        Assert.True(act.MembersActivated >= 1);

        var members = await presets.MembersAsync(existing.Value.Id);
        Assert.Contains("A.Look.1", members);
        Assert.Contains("A.Base.1", members);
    }

    [Fact]
    public async Task Analyze_respects_global_alias_fallback()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "A.Look.1.var", "A", "Look", "Gone.Missing.1");
        WriteVar(repoDir, "Real.Target.1.var", "Real", "Target");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();

        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var look = await db.Packages.FirstAsync(p => p.VarName == "A.Look.1");
        var target = await db.Packages.FirstAsync(p => p.VarName == "Real.Target.1");
        (await db.PackageListItems.FirstAsync(i => i.PackageId == look.Id)).IsActive = true;
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<IAliasService>()
            .SetAsync("Gone.Missing.1", target.Id);

        var analysis = await scope.ServiceProvider.GetRequiredService<IInstalledDepsRepair>().AnalyzeAsync();
        var aliased = Assert.Single(analysis.Entries);
        Assert.True(aliased.InLibrary);
        Assert.Equal(InstalledDepsResolveVia.Alias, aliased.Via);
        Assert.Equal("Real.Target.1", aliased.ResolvedVarName);
        Assert.False(aliased.NeedsAlias);
        Assert.Empty(analysis.Leftovers);
    }

    [Fact]
    public async Task ActivateFromAnalysis_skips_already_active_packages()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "A.Look.1.var", "A", "Look", "A.Base.1");
        WriteVar(repoDir, "A.Base.1.var", "A", "Base");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();

        var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();
        var existing = await presets.CreateAsync("InstalledSet", ["A.Look.1", "A.Base.1"]);
        Assert.True(existing.IsSuccess);

        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        foreach (var name in new[] { "A.Look.1", "A.Base.1" })
        {
            var pkg = await db.Packages.FirstAsync(p => p.VarName == name);
            (await db.PackageListItems.FirstAsync(i => i.PackageId == pkg.Id)).IsActive = true;
        }
        await db.SaveChangesAsync();

        var repair = scope.ServiceProvider.GetRequiredService<IInstalledDepsRepair>();
        var analysis = await repair.AnalyzeAsync();
        Assert.Contains(analysis.Entries, e => e.Ref == "A.Base.1" && e.InLibrary);

        var act = await repair.ActivateFromAnalysisAsync(analysis);
        Assert.Equal(0, act.MembersActivated); // Base already active — no re-add
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
        using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);
        var depObj = string.Join(",", deps.Select(d => "\"" + d + "\":{}"));
        var entry = zip.CreateEntry("meta.json");
        using (var s = entry.Open())
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                "{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\",\"dependencies\":{" + depObj + "}}");
            s.Write(bytes);
        }
        var content = zip.CreateEntry("Custom/Hair/h.vam");
        using (var s = content.Open())
            s.Write(System.Text.Encoding.UTF8.GetBytes("x"));
    }
}
