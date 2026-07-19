using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// BE-N11 · A saved alias maps a missing reference to an owned package; the resolver then resolves it, so
/// the package stops reporting a missing dependency. (16-checklist BE-N11.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class AliasServiceFlowTests
{
    [Fact]
    public async Task Alias_makes_a_missing_dependency_resolve()
    {
        await using var host = TestHost.Create(withPersistence: true);
        var repoId = Guid.NewGuid();

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "r", MountPath = @"C:\r", IsOnline = true, IsEnabled = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            Pkg(db, 1, "A.P.1");                      // depends on the missing ref
            Pkg(db, 2, "Owned.Sub.2");                // the alias target
            db.VarFiles.Add(new VarFile { Id = 10, PackageId = 1, RepositoryId = repoId, RelativePath = "a.var", SizeBytes = 1, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow });
            db.Dependencies.Add(new Dependency
            {
                Id = 1, VarFileId = 10, DependsOnRefKey = IdentityFold.Compute("Gone.Missing.1"),
                DependsOnRefRaw = "Gone.Missing.1", IsMissing = true,
            });
            db.PackageListItems.Add(new PackageListItem { PackageId = 1, VarName = "A.P.1", Creator = "A", PackageName = "P", VersionToken = "1", PrimaryType = ContentType.Scene, AddedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            (await db.Packages.FindAsync(1L))!.CanonicalVarFileId = 10;
            await db.SaveChangesAsync();
        }

        // Before alias: resolve marks the dep missing.
        using (var scope = host.Host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            Assert.True((await db.PackageListItems.FirstAsync(x => x.PackageId == 1)).HasMissingDeps);
        }

        // Save the alias → re-resolve → no longer missing.
        using (var scope = host.Host.Services.CreateScope())
        {
            var alias = scope.ServiceProvider.GetRequiredService<IAliasService>();
            Assert.True((await alias.SetAsync("Gone.Missing.1", 2)).IsSuccess);
            Assert.Contains(await alias.ListAsync(), a => a.MissingRef == "Gone.Missing.1" && a.ResolvedPackageId == 2);

            await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            Assert.False((await db.PackageListItems.FirstAsync(x => x.PackageId == 1)).HasMissingDeps);
        }
    }

    private static void Pkg(VarVaultDbContext db, long id, string name) =>
        db.Packages.Add(new Package
        {
            Id = id, VarName = name, IdentityKey = name.ToUpperInvariant(), Creator = name.Split('.')[0],
            PackageName = name.Split('.')[1], VersionToken = "1", VersionSort = 1,
            FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        });
}
