using System.IO;
using System.IO.Compression;
using System.Text;
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
