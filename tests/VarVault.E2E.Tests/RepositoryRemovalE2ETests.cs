using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;
using static VarVault.E2E.Tests.ImportFixtures;

namespace VarVault.E2E.Tests;

/// <summary>
/// Removing a repository de-registers it from the catalog only: the Repository row + its catalogued vars (and
/// orphaned packages) are gone from the DB, but the folder and every <c>.var</c> file remain untouched on disk.
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class RepositoryRemovalE2ETests
{
    [Fact]
    public async Task Remove_clears_catalog_but_keeps_files_on_disk()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        WriteVar(repoDir.Path, "Creator.PackA.1.var", "Creator", "PackA", [("Custom/a.vam", "A")]);
        WriteVar(repoDir.Path, "Creator.PackB.1.var", "Creator", "PackB", [("Custom/b.vam", "B")]);

        Guid repoId;
        using (var scope = host.Host.Services.CreateScope())
            repoId = (await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("temp", repoDir.Path))).Value.Id;
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        // Sanity: the catalog now holds this repo's vars.
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            Assert.Equal(2, await db.VarFiles.CountAsync(v => v.RepositoryId == repoId));
            Assert.True(await db.Packages.AnyAsync());
        }

        // Remove.
        bool removed;
        using (var scope = host.Host.Services.CreateScope())
            removed = await scope.ServiceProvider.GetRequiredService<IRepositoryService>().RemoveAsync(repoId);
        Assert.True(removed);

        // Catalog: repo + its vars + orphaned packages are gone.
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            Assert.False(await db.Repositories.AnyAsync(r => r.Id == repoId));
            Assert.Equal(0, await db.VarFiles.CountAsync(v => v.RepositoryId == repoId));
            Assert.False(await db.Packages.AnyAsync());          // orphaned packages pruned
        }

        // Disk: the folder + both .var files are still exactly there.
        Assert.True(Directory.Exists(repoDir.Path));
        Assert.True(File.Exists(Path.Combine(repoDir.Path, "Creator.PackA.1.var")));
        Assert.True(File.Exists(Path.Combine(repoDir.Path, "Creator.PackB.1.var")));

        // And it can be registered again (proving it was a clean de-registration).
        using (var scope = host.Host.Services.CreateScope())
        {
            var r = await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("temp-again", repoDir.Path));
            Assert.True(r.IsSuccess);
        }
    }

    [Fact]
    public async Task Remove_unknown_repo_returns_false()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IRepositoryService>().RemoveAsync(Guid.NewGuid()));
    }
}
