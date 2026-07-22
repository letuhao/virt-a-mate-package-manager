using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Presets;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// Loading-preset CRUD + dependency-aware activation preview: members resolve by name and the preview
/// pulls in the forward-dependency closure ("will pull in N"). (Checklist 3.6/3.8.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class PresetFlowTests
{
    [Fact]
    public async Task Create_preset_and_preview_pulls_in_the_dependency_closure()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        // Look depends on Base; Base is standalone; Extra is unrelated.
        WriteVar(repoDir, "A.Look.1.var", "A", "Look", "A.Base.1");
        WriteVar(repoDir, "A.Base.1.var", "A", "Base");
        WriteVar(repoDir, "A.Extra.1.var", "A", "Extra");

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);
        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<VarVault.Domain.Dependencies.IDependencyResolver>().ResolveAllAsync();

        var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();

        // Preset references the Look and a missing package.
        var created = await presets.CreateAsync("MyPreset", ["A.Look.1", "Ghost.Gone.1"]);
        Assert.True(created.IsSuccess, created.Error.ToString());
        Assert.Equal(2, created.Value.MemberCount);

        var list = await presets.ListAsync();
        Assert.Single(list);

        var preview = await presets.PreviewActivationAsync(created.Value.Id);
        Assert.NotNull(preview);
        Assert.Equal(1, preview!.DirectResolved);          // only A.Look.1 resolves
        Assert.Equal(2, preview.TotalWithClosure);         // Look + its dependency Base pulled in
        Assert.Contains("Ghost.Gone.1", preview.MissingRefs);
    }

    [Fact]
    public async Task Preview_pulls_four_level_chain_and_reports_transitive_missing()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "A.Look.1.var", "A", "Look", "A.Mid.1");
        WriteVar(repoDir, "A.Mid.1.var", "A", "Mid", "A.Deep.1");
        WriteVar(repoDir, "A.Deep.1.var", "A", "Deep", "Ghost.Missing.1");

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);
        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<VarVault.Domain.Dependencies.IDependencyResolver>().ResolveAllAsync();
        var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();

        var created = await presets.CreateAsync("Deep", ["A.Look.1"]);
        var preview = await presets.PreviewActivationAsync(created.Value.Id);
        Assert.NotNull(preview);
        Assert.Equal(1, preview!.DirectResolved);
        Assert.Equal(3, preview.TotalWithClosure); // Look + Mid + Deep
        Assert.Contains("Ghost.Missing.1", preview.MissingRefs);
    }

    [Fact]
    public async Task Preview_refreshes_latest_member_after_newer_version_arrives()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "A.Look.1.var", "A", "Look");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using (var scope = host.Host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<VarVault.Domain.Dependencies.IDependencyResolver>().ResolveAllAsync();
            var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();
            var created = await presets.CreateAsync("Latest", ["A.Look.latest"]);
            Assert.True(created.IsSuccess);

            // Snapshot should currently resolve to .1
            var preview1 = await presets.PreviewActivationAsync(created.Value.Id);
            Assert.Equal(1, preview1!.DirectResolved);
            Assert.Equal(1, preview1.TotalWithClosure);
        }

        WriteVar(repoDir, "A.Look.2.var", "A", "Look", "A.Base.1");
        WriteVar(repoDir, "A.Base.1.var", "A", "Base");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using (var scope = host.Host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<VarVault.Domain.Dependencies.IDependencyResolver>().ResolveAllAsync();
            var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();
            var list = await presets.ListAsync();
            var preset = list.Single(p => p.Name == "Latest");

            var preview = await presets.PreviewActivationAsync(preset.Id);
            Assert.NotNull(preview);
            // Re-resolve .latest → Look.2, which pulls Base → total 2
            Assert.Equal(1, preview!.DirectResolved);
            Assert.Equal(2, preview.TotalWithClosure);
        }
    }

    [Fact]
    public async Task Preview_treats_aliased_member_as_resolved_not_missing()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "Real.Target.1.var", "Real", "Target", "Real.Base.1");
        WriteVar(repoDir, "Real.Base.1.var", "Real", "Base");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<VarVault.Domain.Dependencies.IDependencyResolver>().ResolveAllAsync();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var target = await db.Packages.FirstAsync(p => p.VarName == "Real.Target.1");

        var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();
        var created = await presets.CreateAsync("Aliased", ["Renamed.Old.1"]);
        Assert.True(created.IsSuccess);

        db.VarAliases.Add(new VarAlias
        {
            MissingRefKey = VarVault.Domain.Identity.IdentityFold.Compute("Renamed.Old.1"),
            MissingRefRaw = "Renamed.Old.1",
            ResolvedPackageId = target.Id,
            Scope = AliasScope.Global,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var preview = await presets.PreviewActivationAsync(created.Value.Id);
        Assert.NotNull(preview);
        Assert.Equal(1, preview!.DirectResolved);
        Assert.Equal(2, preview.TotalWithClosure); // Target + Base
        Assert.DoesNotContain("Renamed.Old.1", preview.MissingRefs);
    }

    [Fact]
    public async Task Create_rejects_when_all_member_refs_are_invalid()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();

        var created = await presets.CreateAsync("Bad", ["not-a-ref", "also..bad"]);
        Assert.True(created.IsFailure);
        Assert.Equal("preset.member.parse", created.Error.Code);
        Assert.Empty(await presets.ListAsync());
    }

    [Fact]
    public async Task Duplicate_preset_name_is_rejected_and_delete_works()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();

        var a = await presets.CreateAsync("P", []);
        Assert.True(a.IsSuccess);
        Assert.True((await presets.CreateAsync("P", [])).IsFailure); // duplicate name

        Assert.True(await presets.DeleteAsync(a.Value.Id));
        Assert.Empty(await presets.ListAsync());
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
        Add(zip, "Custom/Hair/h.vam", "x");
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
