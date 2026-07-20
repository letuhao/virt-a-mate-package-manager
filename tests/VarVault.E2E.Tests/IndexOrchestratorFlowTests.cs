using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// BE-N0 · Index orchestration is the runtime trigger: registering a repository and calling
/// <see cref="IIndexOrchestrator.IndexAllAsync"/> populates the read model, resolves dependencies (so
/// HasMissingDeps is set), and recomputes usage — the flow that previously only ran in tests.
/// (16-checklist BE-N0.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class IndexOrchestratorFlowTests
{
    [Fact]
    public async Task Index_all_populates_read_model_resolves_and_recomputes()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();

        // A var that declares a dependency not present in the repo → should end up HasMissingDeps=true.
        WriteVar(repoDir, "Creator.PackageA.1.var",
            meta: """{"creatorName":"Creator","packageName":"PackageA","dependencies":{"Gone.Missing.1":{}}}""",
            entries: [("Saves/scene/s.json", "{}")]);
        WriteVar(repoDir, "Creator.PackageB.1.var",
            meta: """{"creatorName":"Creator","packageName":"PackageB"}""",
            entries: [("Custom/Hair/h.vam", "x")]);

        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            var reg = await repos.RegisterAsync(new RegisterRepositoryRequest("test", repoDir.Path));
            Assert.True(reg.IsSuccess);
        }

        IndexRunSummary summary;
        using (var scope = host.Host.Services.CreateScope())
        {
            var orchestrator = scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>();
            summary = await orchestrator.IndexAllAsync();
        }

        Assert.Equal(1, summary.Repositories);
        Assert.Equal(2, summary.Indexed);
        Assert.True(summary.Missing >= 1); // Gone.Missing.1

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            Assert.Equal(2, await db.PackageListItems.CountAsync());
            var a = await db.PackageListItems.FirstAsync(p => p.VarName == "Creator.PackageA.1");
            Assert.True(a.HasMissingDeps);   // resolver populated the bit (BE-N0 fix)
            var b = await db.PackageListItems.FirstAsync(p => p.VarName == "Creator.PackageB.1");
            Assert.False(b.HasMissingDeps);
        }
    }

    // D: exercise the orchestrator against a real repo when present (loop repos).
    [Theory]
    [MemberData(nameof(TestCorpus.ConfiguredRoots), MemberType = typeof(TestCorpus))]
    public async Task Index_all_on_a_real_repo(string repoPath)
    {
        if (string.IsNullOrEmpty(repoPath))
            return; // no VARVAULT_TEST_CORPUS* configured

        await using var host = TestHost.Create(withPersistence: true);
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            await repos.RegisterAsync(new RegisterRepositoryRequest("real", repoPath));
        }

        IndexRunSummary summary;
        using (var scope = host.Host.Services.CreateScope())
            summary = await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        Assert.True(summary.Indexed > 0, $"expected to index vars from {repoPath}");
        using var read = host.Host.Services.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        Assert.True(await db.PackageListItems.AnyAsync());
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
