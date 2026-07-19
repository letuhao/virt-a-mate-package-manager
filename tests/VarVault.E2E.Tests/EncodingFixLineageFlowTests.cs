using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// End-to-end encoding fix with lineage: a GBK-broken var is indexed (NeedsFix), fixed into a new
/// UTF-8 var, and the catalog records FixedFromVarFileId / SupersededByVarFileId — original retained.
/// (IDX-9; checklist 4.14.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class EncodingFixLineageFlowTests
{
    private const int Gbk = 936;

    [Fact]
    public async Task Fix_indexes_a_fixed_var_and_records_lineage()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteBrokenGbkVar(Path.Combine(repoDir.Path, "Creator.Broken.1.var"));
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var broken = await db.VarFiles.FirstAsync();
        Assert.Equal(EncodingHealth.NeedsFix, broken.EncodingHealth);
        Assert.Equal("GBK", broken.DetectedCodepage);

        var outputPath = Path.Combine(repoDir.Path, "Creator.Broken.1.fixed.var");
        var result = await scope.ServiceProvider.GetRequiredService<EncodingFixCoordinator>().FixAsync(broken.Id, outputPath);
        Assert.True(result.IsSuccess, result.Error.ToString());

        // Lineage recorded; original retained.
        var fixedVar = await db.VarFiles.FirstAsync(v => v.Id == result.Value);
        Assert.Equal(EncodingHealth.Fixed, fixedVar.EncodingHealth);
        Assert.Equal(broken.Id, fixedVar.FixedFromVarFileId);

        var reloadedBroken = await db.VarFiles.FirstAsync(v => v.Id == broken.Id);
        Assert.Equal(fixedVar.Id, reloadedBroken.SupersededByVarFileId);
        Assert.True(File.Exists(Path.Combine(repoDir.Path, "Creator.Broken.1.var"))); // original file retained
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

    private static void WriteBrokenGbkVar(string path)
    {
        var gbk = Encoding.GetEncoding(Gbk);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false, gbk);
        Add(zip, "meta.json", "{\"creatorName\":\"Creator\",\"packageName\":\"Broken\"}");
        Add(zip, "Custom/Clothing/衣装/裙子.vam", "x");
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
