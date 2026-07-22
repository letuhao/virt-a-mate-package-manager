using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Safety;
using VarVault.Sdk.Import;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

[Trait("Category", TestCategories.E2E)]
public sealed class ImportProfileTidyE2ETests
{
    [Fact]
    public async Task Scan_skips_vars_inside_VarsLink_farm()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var profile = new TempDirectory();
        WriteMinimalVar(Path.Combine(profile.Path, "Loose.Download.1.var"), "Loose", "Download");
        var farm = Path.Combine(profile.Path, "___VarsLink___");
        Directory.CreateDirectory(farm);
        WriteMinimalVar(Path.Combine(farm, "Should.Skip.1.var"), "Should", "Skip");

        using var scope = host.Host.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<IImportService>()
            .ScanAsync(new ImportSpec([profile.Path], Guid.NewGuid()));

        Assert.Single(session.Items);
        Assert.Equal("Loose.Download.1.var", session.Items[0].FileName);
        Assert.Equal(1, session.Sources.Single().VarCount);
    }

    [Fact]
    public async Task Apply_reports_copied_incoming_paths_for_loose_sources()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var profile = new TempDirectory();
        WriteMinimalVar(Path.Combine(profile.Path, "Fresh.Look.1.var"), "Fresh", "Look");

        Guid repoId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repo = await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("hot", repoDir.Path));
            Assert.True(repo.IsSuccess);
            repoId = repo.Value.Id;
        }

        using var applyScope = host.Host.Services.CreateScope();
        var import = applyScope.ServiceProvider.GetRequiredService<IImportService>();
        var session = await import.ScanAsync(new ImportSpec([profile.Path], repoId));
        Assert.Single(session.Items);
        session.Items[0].Decision = ImportDecision.Import;

        var result = await import.ApplyAsync(session);
        Assert.Equal(1, result.Copied);
        Assert.NotNull(result.CopiedIncomingPaths);
        Assert.Contains(result.CopiedIncomingPaths!, p => p.EndsWith("Fresh.Look.1.var", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(profile.Path, "Fresh.Look.1.var")), "source still present until Trash");
        Assert.True(File.Exists(Path.Combine(repoDir.Path, "Fresh.Look.1.var")));
    }

    [Fact]
    public async Task Trash_service_moves_loose_original_after_copy()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var profile = new TempDirectory();
        var loose = Path.Combine(profile.Path, "Fresh.Look.1.var");
        WriteMinimalVar(loose, "Fresh", "Look");

        using var scope = host.Host.Services.CreateScope();
        var trash = scope.ServiceProvider.GetRequiredService<ITrashService>();
        var r = await trash.TrashAsync(loose, "import.tidy-original");
        Assert.True(r.IsSuccess);
        Assert.False(File.Exists(loose));
    }

    private static void WriteMinimalVar(string path, string creator, string package)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("meta.json", CompressionLevel.Optimal);
        using var s = entry.Open();
        var json = $"{{\"creatorName\":\"{creator}\",\"packageName\":\"{package}\",\"dependencies\":{{}}}}";
        s.Write(Encoding.UTF8.GetBytes(json));
    }
}
