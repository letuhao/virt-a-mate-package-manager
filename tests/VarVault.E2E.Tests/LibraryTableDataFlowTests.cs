using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// doc 26 · G-2.3 / G-2.2 — the library table's numeric Dep column and Added column are backed by real
/// data: indexing a var that declares dependencies populates <see cref="PackageListEntry.DependencyCount"/>
/// (via a secondary query over the canonical var's dependency rows) and <see cref="PackageListEntry.AddedAt"/>.
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class LibraryTableDataFlowTests
{
    [Fact]
    public async Task Library_row_exposes_dependency_count_and_added_date()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();

        // A scene var declaring two forward dependencies (absent → still counted as declared deps).
        WriteVar(repoDir, "Creator.Scene.1.var",
            meta: """{"creatorName":"Creator","packageName":"Scene","dependencies":{"Dep.One.1":{},"Dep.Two.1":{}}}""",
            entries: [("Saves/scene/s.json", "{}")]);

        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            Assert.True((await repos.RegisterAsync(new RegisterRepositoryRequest("t", repoDir.Path))).IsSuccess);
        }
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        using var read = host.Host.Services.CreateScope();
        var page = await read.ServiceProvider.GetRequiredService<ILibraryQueryService>()
            .GetPageAsync(new LibraryQuery());

        var row = Assert.Single(page.Items);
        Assert.Equal("Creator.Scene.1", row.VarName);
        Assert.Equal(2, row.DependencyCount);        // G-2.3 — both declared deps counted
        Assert.NotNull(row.AddedAt);                 // G-2.2 — indexing stamps AddedAt
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
