using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// BE-N8 · Proposals aggregate dedup + encoding + stale, and Approve dispatches to the right runner
/// (reclaim/health/trash). Nothing runs unattended. (16-checklist BE-N8.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ProposalServiceFlowTests
{
    [Fact]
    public async Task Lists_proposals_and_approve_dispatches()
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
            // Dedup group: 3 content-identical online copies.
            Pkg(db, 1, "A.P.1", vsort: 1);
            db.VarFiles.Add(Dup(10, 1, repoId, "a1.var", "sigA", "hashA"));
            db.VarFiles.Add(Dup(11, 1, repoId, "a2.var", "sigA", "hashA"));
            db.VarFiles.Add(Dup(12, 1, repoId, "a3.var", "sigA", "hashA"));
            // Stale: B.P.1 (old, no reverse deps) superseded by B.P.2.
            Pkg(db, 2, "B.P.1", vsort: 1);
            db.VarFiles.Add(Dup(20, 2, repoId, "b1.var", "sigB", "hashB"));
            Pkg(db, 3, "B.P.2", vsort: 2);
            // Encoding: a GBK-flagged var.
            Pkg(db, 4, "C.P.1", vsort: 1);
            var enc = Dup(30, 4, repoId, "c1.var", "sigC", "hashC");
            enc.EncodingHealth = EncodingHealth.NeedsFix;
            enc.DetectedCodepage = "GBK";
            db.VarFiles.Add(enc);
            await db.SaveChangesAsync();

            // Set canonical after the var exists (avoids the Package↔VarFile insert cycle).
            (await db.Packages.FindAsync(2L))!.CanonicalVarFileId = 20;
            await db.SaveChangesAsync();
        }

        using var run = host.Host.Services.CreateScope();
        var svc = run.ServiceProvider.GetRequiredService<IProposalService>();

        var proposals = await svc.ListAsync();
        Assert.Contains(proposals, p => p.Kind == ProposalKind.Dedup);
        Assert.Contains(proposals, p => p.Kind == ProposalKind.EncodingFix);
        Assert.Contains(proposals, p => p.Kind == ProposalKind.RetireStale);

        var dedup = proposals.First(p => p.Kind == ProposalKind.Dedup);
        var approve = await svc.ApproveAsync(dedup);
        Assert.True(approve.Ok, approve.Message);
        Assert.False(File.Exists(Path.Combine(repo.Path, "a2.var"))); // redundant trashed
        Assert.True(File.Exists(Path.Combine(repo.Path, "a1.var")));  // one kept

        var stale = proposals.First(p => p.Kind == ProposalKind.RetireStale);
        Assert.True((await svc.ApproveAsync(stale)).Ok);
        Assert.False(File.Exists(Path.Combine(repo.Path, "b1.var"))); // old version retired to trash

        Assert.True((await svc.RejectAsync(dedup)).Ok);
    }

    private static void Pkg(VarVaultDbContext db, long id, string name, long vsort, long? canonical = null) =>
        db.Packages.Add(new Package
        {
            Id = id, VarName = name, IdentityKey = name.ToUpperInvariant(), Creator = name.Split('.')[0],
            PackageName = name.Split('.')[1], VersionToken = vsort.ToString(), VersionSort = vsort,
            CanonicalVarFileId = canonical, ReverseDependentCount = 0,
            FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        });

    private static VarFile Dup(long id, long pkgId, Guid repoId, string rel, string sig, string hash) => new()
    {
        Id = id, PackageId = pkgId, RepositoryId = repoId, RelativePath = rel, SizeBytes = 100,
        ContentSignature = sig, ContentHash = hash, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
    };
}
