using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// Staged 2-pass indexing: pass-1 (names/meta/deps/counts + read model) makes the catalog browsable
/// before pass-2 (previews) runs; pass-2 fills thumbnails in afterwards. (Checklist 1.23/1.32.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class StagedIndexingFlowTests
{
    [Fact]
    public async Task Catalog_is_browsable_before_pass_two_runs()
    {
        // A spy pass-2 records how many read-model rows already exist when it is invoked.
        var probe = new BrowsableProbe();
        await using var host = TestHost.Create(withPersistence: true, configure: services =>
        {
            services.AddSingleton(probe);
            services.RemoveAll<IPreviewIndexer>();
            services.AddScoped<IPreviewIndexer, SpyPreviewIndexer>();
        });

        using var repoDir = new TempDirectory();
        var repoId = await RegisterRepository(host, repoDir.Path);
        WriteVar(repoDir, "A.One.1.var", """{"creatorName":"A","packageName":"One"}""", [("Saves/scene/s.json", "{}")]);
        WriteVar(repoDir, "A.Two.1.var", """{"creatorName":"A","packageName":"Two"}""", [("Saves/scene/s.json", "{}")]);

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        // Pass-2 saw both PackageListItem rows already materialized → the catalog was browsable first.
        Assert.Equal(2, probe.ListItemsAtPass2);
    }

    [Fact]
    public async Task Pass_two_extracts_and_stores_the_sibling_preview()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await RegisterRepository(host, repoDir.Path);

        // A scene with a sibling .jpg preview beside it.
        WriteVar(repoDir, "A.Scene.1.var", """{"creatorName":"A","packageName":"Scene"}""",
            [("Saves/scene/s.json", "{}"), ("Saves/scene/s.jpg", "JPEGBYTES")]);

        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        long packageId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            var item = await db.PackageListItems.SingleAsync();
            packageId = item.PackageId;
            Assert.Equal("thumb:" + packageId, item.PreviewThumbRef); // pass-2 stamped the ref
        }

        var thumbs = host.Host.Services.GetRequiredService<IThumbnailStore>();
        Assert.True(await thumbs.ExistsAsync(packageId));
        var bytes = await thumbs.GetAsync(packageId);
        Assert.Equal("JPEGBYTES", Encoding.UTF8.GetString(bytes!));
    }

    [Fact]
    public async Task Indexing_reports_a_determinate_count_and_ticks_progress()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await RegisterRepository(host, repoDir.Path);
        for (var i = 0; i < 5; i++)
            WriteVar(repoDir, $"A.Pkg{i}.1.var", $$"""{"creatorName":"A","packageName":"Pkg{{i}}"}""", [("Saves/scene/s.json", "{}")]);

        var sink = new CapturingSink();
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path, sink);

        // Progress must be reported *during* the run, not only at the end.
        Assert.NotEmpty(sink.Reports);
        // The total (denominator) becomes known — a determinate bar, not "0/0" the whole time.
        Assert.Contains(sink.Reports, r => r.Total == 5);
        // Done advances to cover every enumerated var (the bar reaches the end).
        Assert.Contains(sink.Reports, r => r.Total == 5 && r.Done == 5);
        // A human-readable phase message rides along (drives the log-dock / job sub-line).
        Assert.Contains(sink.Reports, r => r.Message is { Length: > 0 });
    }

    private sealed class CapturingSink : VarVault.Common.IProgressSink
    {
        public List<VarVault.Common.ProgressReport> Reports { get; } = new();
        public void Report(VarVault.Common.ProgressReport report) { lock (Reports) Reports.Add(report); }
    }

    private sealed class BrowsableProbe { public int ListItemsAtPass2 = -1; }

    private sealed class SpyPreviewIndexer(VarVaultDbContext db, BrowsableProbe probe) : IPreviewIndexer
    {
        public async Task<int> BuildPreviewsAsync(IReadOnlyCollection<long> packageIds, CancellationToken cancellationToken = default)
        {
            probe.ListItemsAtPass2 = await db.PackageListItems.CountAsync(cancellationToken);
            return 0;
        }
    }

    private static async Task<Guid> RegisterRepository(TestHost host, string path)
    {
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var id = Guid.NewGuid();
        db.Repositories.Add(new Repository
        {
            Id = id, Name = "test", MountPath = path, IsOnline = true, IsEnabled = true,
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
        AddEntry(zip, "meta.json", meta);
        foreach (var (name, content) in entries)
            AddEntry(zip, name, content);
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
