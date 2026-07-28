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

    [Fact]
    public async Task Apply_reports_exact_skip_paths_for_profile_tidy()
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

            // Seed the library with the same content so scan classifies Exact.
            var import = scope.ServiceProvider.GetRequiredService<IImportService>();
            var seed = await import.ScanAsync(new ImportSpec([profile.Path], repoId));
            Assert.Single(seed.Items);
            seed.Items[0].Decision = ImportDecision.Import;
            var seeded = await import.ApplyAsync(seed);
            Assert.Equal(1, seeded.Copied);
        }

        // Re-drop a loose Exact duplicate into the profile and Apply with Skip.
        WriteMinimalVar(Path.Combine(profile.Path, "Fresh.Look.1.var"), "Fresh", "Look");
        using var applyScope = host.Host.Services.CreateScope();
        var svc = applyScope.ServiceProvider.GetRequiredService<IImportService>();
        var session = await svc.ScanAsync(new ImportSpec([profile.Path], repoId));
        Assert.Single(session.Items);
        Assert.Equal(ImportLane.Exact, session.Items[0].Lane);
        session.Items[0].Decision = ImportDecision.Skip;

        var result = await svc.ApplyAsync(session);
        Assert.Equal(0, result.Copied);
        Assert.Equal(1, result.Skipped);
        Assert.NotNull(result.CopiedIncomingPaths);
        Assert.Contains(result.CopiedIncomingPaths!, p => p.EndsWith("Fresh.Look.1.var", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Apply_exact_skip_does_not_tidy_when_repo_file_missing_on_disk()
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

            var import = scope.ServiceProvider.GetRequiredService<IImportService>();
            var seed = await import.ScanAsync(new ImportSpec([profile.Path], repoId));
            Assert.Single(seed.Items);
            seed.Items[0].Decision = ImportDecision.Import;
            var seeded = await import.ApplyAsync(seed);
            Assert.Equal(1, seeded.Copied);
        }

        // Catalog still has the row, but the surviving copy is gone from disk.
        var repoCopy = Path.Combine(repoDir.Path, "Fresh.Look.1.var");
        Assert.True(File.Exists(repoCopy));
        File.Delete(repoCopy);
        Assert.False(File.Exists(repoCopy));

        WriteMinimalVar(Path.Combine(profile.Path, "Fresh.Look.1.var"), "Fresh", "Look");
        using var applyScope = host.Host.Services.CreateScope();
        var svc = applyScope.ServiceProvider.GetRequiredService<IImportService>();
        var session = await svc.ScanAsync(new ImportSpec([profile.Path], repoId));
        Assert.Single(session.Items);
        Assert.Equal(ImportLane.Exact, session.Items[0].Lane);
        session.Items[0].Decision = ImportDecision.Skip;

        var result = await svc.ApplyAsync(session);
        Assert.Equal(0, result.Copied);
        Assert.Equal(1, result.Skipped);
        Assert.True(result.CopiedIncomingPaths is null || result.CopiedIncomingPaths.Count == 0,
            "Must not tidy the only remaining copy when the repo file is missing on disk");
        Assert.True(File.Exists(Path.Combine(profile.Path, "Fresh.Look.1.var")));
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
