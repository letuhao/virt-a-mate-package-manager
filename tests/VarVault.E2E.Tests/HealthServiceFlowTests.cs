using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// BE-N5 · Health: encoding-fix groups by codepage, and fixing a broken var into a new UTF-8 var with the
/// original retained. (16-checklist BE-N5.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class HealthServiceFlowTests
{
    private const int Gbk = 936;

    [Fact]
    public async Task Encoding_groups_then_fix_creates_utf8_var()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = Guid.NewGuid();

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "t", MountPath = repoDir.Path, IsOnline = true, IsEnabled = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        WriteBrokenGbkVar(Path.Combine(repoDir.Path, "Creator.Broken.1.var"));
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        long brokenId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IHealthService>();
            var groups = await svc.EncodingGroupsAsync();
            Assert.Contains(groups, g => g.Codepage == "GBK" && g.Count == 1);

            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            brokenId = (await db.VarFiles.FirstAsync(v => v.EncodingHealth == EncodingHealth.NeedsFix)).Id;

            var fix = await svc.FixAsync(brokenId);
            Assert.True(fix.IsSuccess, fix.Error.ToString());
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            Assert.True(await db.VarFiles.AnyAsync(v => v.EncodingHealth == EncodingHealth.Fixed));
            Assert.True(File.Exists(Path.Combine(repoDir.Path, "Creator.Broken.1.var")));       // original retained
            Assert.True(File.Exists(Path.Combine(repoDir.Path, "Creator.Broken.1.fixed.var"))); // fixed sibling
        }
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
