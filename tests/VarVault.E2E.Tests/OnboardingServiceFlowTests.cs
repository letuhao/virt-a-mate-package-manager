using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// BE-N13 · Onboarding registers a folder (media/tier detected), applies the reserve, and indexes it.
/// (16-checklist BE-N13.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class OnboardingServiceFlowTests
{
    [Fact]
    public async Task Add_and_index_registers_detects_and_populates()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        WriteVar(Path.Combine(repoDir.Path, "Creator.Pkg.1.var"), "Creator", "Pkg");

        OnboardingResult result;
        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOnboardingService>();
            var r = await svc.AddAndIndexAsync("my-repo", repoDir.Path, reserveBytes: 200L * 1024 * 1024);
            Assert.True(r.IsSuccess, r.Error.ToString());
            result = r.Value;
        }

        Assert.Equal("my-repo", result.Repository.Name);
        Assert.True(result.Repository.Tier is >= 1 and <= 3);   // a tier was assigned
        Assert.Equal(1, result.Index.Indexed);

        using var read = host.Host.Services.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        Assert.True(await db.PackageListItems.AnyAsync());       // catalog populated
    }

    [Fact]
    public async Task Add_and_index_a_real_repo()
    {
        if (TestCorpus.Primary is not { } path)
            return;

        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var r = await scope.ServiceProvider.GetRequiredService<IOnboardingService>().AddAndIndexAsync("real", path);
        Assert.True(r.IsSuccess);
        Assert.True(r.Value.Index.Indexed > 0);
    }

    private static void WriteVar(string path, string creator, string pkg)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var meta = zip.CreateEntry("meta.json");
        using (var s = meta.Open()) s.Write(Encoding.UTF8.GetBytes($"{{\"creatorName\":\"{creator}\",\"packageName\":\"{pkg}\"}}"));
        var entry = zip.CreateEntry("Custom/Hair/h.vam");
        using (var s = entry.Open()) s.Write(Encoding.UTF8.GetBytes("x"));
    }
}
