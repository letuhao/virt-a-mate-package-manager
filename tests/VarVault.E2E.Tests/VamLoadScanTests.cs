using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.Fingerprinting;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

[Trait("Category", TestCategories.E2E)]
public sealed class VamLoadScanTests
{
    [Fact]
    public async Task Scan_installed_flags_case_twin_duplicate_entries()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "t", MountPath = repoDir.Path, IsOnline = true, IsEnabled = true,
                CreatedAt = now, UpdatedAt = now,
            });
            db.Profiles.Add(new Profile
            {
                Name = "active", DirPath = Path.Combine(repoDir.Path, "profile"), IsActive = true,
                CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        WriteCaseTwinVar(Path.Combine(repoDir.Path, "Creator.DupKeys.1.var"));
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            var pkg = await db.Packages.SingleAsync();
            var profile = await db.Profiles.SingleAsync(p => p.IsActive);
            db.ProfilePackageLinks.Add(new ProfilePackageLink
            {
                ProfileId = profile.Id,
                PackageId = pkg.Id,
                InstalledAt = now,
                Reason = ActivationReason.Explicit,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IHealthService>();
            var issues = await svc.ScanVamLoadAsync(VamLoadScanScope.InstalledOnly);
            Assert.Contains(issues, i => i.Kind == "DuplicateEntries" && i.VarName.Contains("DupKeys", StringComparison.Ordinal));

            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            var vf = await db.VarFiles.SingleAsync();
            Assert.Equal(IntegrityStatus.DuplicateEntries, vf.IntegrityStatus);

            var integrity = await svc.IntegrityAsync();
            Assert.Contains(integrity, i => i.VarFileId == vf.Id && i.Kind == "DuplicateEntries");
        }
    }

    [Fact]
    public async Task FixDuplicateEntries_writes_dedup_sibling_and_clears_status()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "t", MountPath = repoDir.Path, IsOnline = true, IsEnabled = true,
                CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        WriteCaseTwinVar(Path.Combine(repoDir.Path, "Creator.DupKeys.1.var"));
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        long brokenId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            brokenId = (await db.VarFiles.SingleAsync()).Id;
            Assert.Equal(IntegrityStatus.DuplicateEntries, (await db.VarFiles.SingleAsync()).IntegrityStatus);

            var svc = scope.ServiceProvider.GetRequiredService<IHealthService>();
            var fix = await svc.FixDuplicateEntriesAsync(brokenId);
            Assert.True(fix.IsSuccess, fix.IsFailure ? fix.Error.ToString() : "");
            Assert.True(File.Exists(Path.Combine(repoDir.Path, "Creator.DupKeys.1.dedup.var")));
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            var broken = await db.VarFiles.SingleAsync(v => v.Id == brokenId);
            Assert.NotNull(broken.SupersededByVarFileId);
            var fixedVar = await db.VarFiles.SingleAsync(v => v.Id == broken.SupersededByVarFileId);
            Assert.Equal(IntegrityStatus.Ok, fixedVar.IntegrityStatus);
        }
    }

    [Fact]
    public void Encoding_only_defects_are_filtered_from_vam_load_report()
    {
        var entries = new[]
        {
            new ZipEntryFacts("meta.json"u8.ToArray(), 1, 0, false, true),
            new ZipEntryFacts("Custom/a.vam"u8.ToArray(), 1, 0, false, true),
        };
        var defects = VamLoadDefectDetector.Detect(entries, encodingHealth: EncodingHealth.NeedsFix);
        Assert.Contains(defects, d => d.Kind == VamLoadDefectKind.EncodingNeedsFix);
        var structural = defects.Where(d => d.Kind is not VamLoadDefectKind.EncodingNeedsFix).ToList();
        Assert.Empty(structural);
    }

    private static void WriteCaseTwinVar(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "meta.json", "{\"creatorName\":\"Creator\",\"packageName\":\"DupKeys\",\"packageVersion\":\"1\"}");
        Add(zip, "Custom/Foo.vam", "a");
        Add(zip, "custom/foo.vam", "b");
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
