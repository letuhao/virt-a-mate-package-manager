using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Safety;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

[Trait("Category", TestCategories.E2E)]
public sealed class VarMetaEditE2ETests
{
    [Fact]
    public async Task Save_rewrites_meta_trashes_original_and_updates_catalog_deps()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();

        WriteVar(repoDir.Path, "A.Look.1.var", "A", "Look", ["B.Base.1", "Ghost.Missing.1"]);
        Guid repoId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            repoId = (await repos.RegisterAsync(new RegisterRepositoryRequest("repo", repoDir.Path))).Value.Id;
        }
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        long packageId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            packageId = await db.Packages.Where(p => p.VarName == "A.Look.1").Select(p => p.Id).SingleAsync();
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var meta = scope.ServiceProvider.GetRequiredService<IVarMetaEditService>();
            var draft = await meta.LoadAsync(packageId);
            Assert.True(draft.IsSuccess, draft.Error.ToString());
            Assert.Contains("B.Base.1", draft.Value.DependencyRefs);
            Assert.Contains("Ghost.Missing.1", draft.Value.DependencyRefs);

            var save = await meta.SaveAsync(new VarMetaEditRequest(
                packageId,
                draft.Value.VarFileId,
                "A",
                "Look",
                "CC BY",
                "edited",
                "1.20",
                ["B.Base.1", "C.Extra.2"]));
            Assert.True(save.IsSuccess, save.Error.ToString());
            Assert.False(string.IsNullOrEmpty(save.Value.TrashId));
        }

        var path = Path.Combine(repoDir.Path, "A.Look.1.var");
        Assert.True(File.Exists(path));
        using (var zip = ZipFile.OpenRead(path))
        {
            await using var s = zip.GetEntry("meta.json")!.Open();
            using var reader = new StreamReader(s, Encoding.UTF8);
            var text = await reader.ReadToEndAsync();
            Assert.Contains("C.Extra.2", text);
            Assert.Contains("edited", text);
            Assert.DoesNotContain("Ghost.Missing.1", text);
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var trash = scope.ServiceProvider.GetRequiredService<ITrashService>();
            var items = await trash.ListAsync();
            Assert.Contains(items, t => t.Reason == "meta-edit");

            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            var package = await db.Packages.SingleAsync(p => p.Id == packageId);
            Assert.Equal("edited", package.Description);
            Assert.Equal("CC BY", package.LicenseType);

            var metaDeps = await db.Dependencies
                .Where(d => d.VarFileId == package.CanonicalVarFileId && d.RefKind == Domain.Entities.RefKind.Meta)
                .Select(d => d.DependsOnRefRaw)
                .ToListAsync();
            Assert.Contains("B.Base.1", metaDeps);
            Assert.Contains("C.Extra.2", metaDeps);
            Assert.DoesNotContain("Ghost.Missing.1", metaDeps);

            // Embedded-only refs must survive a meta rewrite (full Meta+Embedded replace).
            var embedded = await db.Dependencies
                .Where(d => d.VarFileId == package.CanonicalVarFileId && d.RefKind == Domain.Entities.RefKind.Embedded)
                .Select(d => d.DependsOnRefRaw)
                .ToListAsync();
            Assert.Contains("Embed.Only.1", embedded);
        }
    }

    private static void WriteVar(string dir, string fileName, string creator, string package, IReadOnlyList<string> deps)
    {
        var depJson = string.Join(",", deps.Select(d => $"\"{d}\":{{\"licenseType\":\"\",\"dependencies\":{{}}}}"));
        var meta = $"{{\"creatorName\":\"{creator}\",\"packageName\":\"{package}\",\"dependencies\":{{{depJson}}}}}";
        using var fs = new FileStream(Path.Combine(dir, fileName), FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using (var s = zip.CreateEntry("meta.json").Open())
            s.Write(Encoding.UTF8.GetBytes(meta));
        // Scene JSON with an embedded package ref that is NOT listed in meta.json.
        using (var s = zip.CreateEntry("Saves/scene/x.json").Open())
            s.Write(Encoding.UTF8.GetBytes("""{"a":"Embed.Only.1:/Custom/x.vam"}"""));
        using (var s = zip.CreateEntry("Custom/n.vam").Open())
            s.Write(Encoding.UTF8.GetBytes("n"));
    }
}
