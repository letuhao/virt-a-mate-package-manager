using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// BE-N4 · Reclaim: exact duplicate groups; trashing keeps one copy and removes hash-verified redundant
/// copies, but never the last copy (single-copy protected). (16-checklist BE-N4.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ReclaimServiceFlowTests
{
    [Fact]
    public async Task Groups_exact_duplicates_and_trashes_only_redundant_verified_copies()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = Guid.NewGuid();

        foreach (var n in new[] { "a1", "a2", "a3", "b1" })
            await File.WriteAllTextAsync(Path.Combine(repo.Path, $"{n}.var"), n);

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "r", MountPath = repo.Path, Tier = 1, MediaType = MediaType.Hdd,
                IsOnline = true, IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            // Package A: 3 content-identical online copies (same signature + hash).
            SeedPackage(db, 1, "A.P.1");
            db.VarFiles.Add(Dup(1, 1, repoId, "a1.var", "sigA", "hashA"));
            db.VarFiles.Add(Dup(2, 1, repoId, "a2.var", "sigA", "hashA"));
            db.VarFiles.Add(Dup(3, 1, repoId, "a3.var", "sigA", "hashA"));
            // Package B: single copy.
            SeedPackage(db, 2, "B.P.1");
            db.VarFiles.Add(Dup(4, 2, repoId, "b1.var", "sigB", "hashB"));
            await db.SaveChangesAsync();
        }

        using var run = host.Host.Services.CreateScope();
        var svc = run.ServiceProvider.GetRequiredService<IReclaimService>();

        var groups = await svc.ExactGroupsAsync();
        Assert.Single(groups);                       // only package A has duplicates
        Assert.Equal(3, groups[0].Copies.Count);

        var reclaim = await svc.TrashRedundantAsync(keepVarFileId: 1, trashVarFileIds: [2, 3]);
        Assert.Equal(2, reclaim.Trashed);
        Assert.Equal(0, reclaim.Blocked);
        Assert.True(File.Exists(Path.Combine(repo.Path, "a1.var")));   // kept
        Assert.False(File.Exists(Path.Combine(repo.Path, "a2.var")));  // trashed
        Assert.False(File.Exists(Path.Combine(repo.Path, "a3.var")));

        // Single copy is protected — cannot trash the last copy of an identity.
        var blocked = await svc.TrashRedundantAsync(keepVarFileId: 4, trashVarFileIds: [4]);
        Assert.Equal(0, blocked.Trashed);
        Assert.Equal(1, blocked.Blocked);
        Assert.True(File.Exists(Path.Combine(repo.Path, "b1.var")));   // still there
    }

    private static void SeedPackage(VarVaultDbContext db, long id, string name) =>
        db.Packages.Add(new Package
        {
            Id = id, VarName = name, IdentityKey = name.ToUpperInvariant(), Creator = name.Split('.')[0],
            PackageName = name, VersionToken = "1", VersionSort = 1,
            FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        });

    private static VarFile Dup(long id, long pkgId, Guid repoId, string rel, string sig, string hash) => new()
    {
        Id = id, PackageId = pkgId, RepositoryId = repoId, RelativePath = rel, SizeBytes = 100,
        ContentSignature = sig, ContentHash = hash, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
    };
}
