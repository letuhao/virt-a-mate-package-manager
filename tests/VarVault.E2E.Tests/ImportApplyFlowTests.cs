using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Content;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Import;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;
using static VarVault.E2E.Tests.ImportFixtures;

namespace VarVault.E2E.Tests;

/// <summary>
/// Slice C proof (doc 31 Phase 5): applying a scanned session copies approved vars into the target repo (durably),
/// fixes CJK encoding on the way, renames name≠meta vars to their meta identity, skips dups, discards corrupt,
/// indexes the new vars into the catalog, records a history run, and cleans the temp workspace. Deterministic
/// (temp catalog + a separate temp target repo; nothing real is touched).
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ImportApplyFlowTests
{
    [Fact]
    public async Task Apply_copies_fixes_renames_skips_discards_and_records_history()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var catalogDir = new TempDirectory();   // the existing library (for dedup)
        using var targetDir = new TempDirectory();     // where imports land (empty repo)
        using var importDir = new TempDirectory();

        // catalog holds Creator.PackA.1 and Creator.PackB.1
        WriteVar(catalogDir.Path, "Creator.PackA.1.var", "Creator", "PackA", [("Custom/a.vam", "AAA")]);
        WriteVar(catalogDir.Path, "Creator.PackB.1.var", "Creator", "PackB", [("Custom/b1.vam", "CCC")]);

        Guid targetRepoId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            await repos.RegisterAsync(new RegisterRepositoryRequest("catalog", catalogDir.Path));
            var target = await repos.RegisterAsync(new RegisterRepositoryRequest("target", targetDir.Path));
            targetRepoId = target.Value.Id;
        }
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        // import folder: one of each actionable lane
        WriteVar(importDir.Path, "Fresh.Look.1.var", "Fresh", "Look", [("Custom/n.vam", "NEW")]);                    // New → Import
        WriteVar(importDir.Path, "Creator.PackB.1.var", "Creator", "PackB", [("Custom/b1.vam", "CCC"), ("Custom/b2.vam", "X")]); // Conflict → KeepIncoming
        WriteVar(importDir.Path, "Creator.PackA.1.var", "Creator", "PackA", [("Custom/a.vam", "AAA")]);              // Exact → Skip
        WriteVar(importDir.Path, "Wrong.Name.1.var", "Totally", "Different", [("Custom/x.vam", "X")]);               // Naming → RenameToMeta
        WriteGbkVar(Path.Combine(importDir.Path, "Gbk.Pack.1.var"), "Gbk", "Pack");                                  // Cjk → ImportAndFix
        WriteBadZip(Path.Combine(importDir.Path, "broken.var"));                                                     // Corrupt → Discard

        // scan → accept recommendations for the review lanes
        ApplyResult result;
        string tempRoot;
        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            var session = await svc.ScanAsync(new ImportSpec([importDir.Path], targetRepoId), progress: null!);
            foreach (var item in session.Items.Where(i => i.Decision == ImportDecision.None))
                item.Decision = item.Recommendation; // accept-all
            tempRoot = session.TempRoot;
            result = await svc.ApplyAsync(session);
        }

        // ── on-disk effects in the target repo ──────────────────────────────────────────────────────────
        Assert.True(File.Exists(Path.Combine(targetDir.Path, "Fresh.Look.1.var")));                 // New imported
        Assert.True(File.Exists(Path.Combine(targetDir.Path, "Creator.PackB.1.var")));              // conflict kept-incoming
        Assert.True(File.Exists(Path.Combine(targetDir.Path, "Totally.Different.1.var")));          // renamed to meta identity
        Assert.True(File.Exists(Path.Combine(targetDir.Path, "Gbk.Pack.1.var")));                   // cjk imported
        Assert.False(File.Exists(Path.Combine(targetDir.Path, "Wrong.Name.1.var")));                // NOT under its bad name
        Assert.False(File.Exists(Path.Combine(targetDir.Path, "broken.var")));                      // discarded

        // ── CJK fix applied: the imported var has no legacy-encoded entries left ─────────────────────────
        using (var scope = host.Host.Services.CreateScope())
        {
            var insp = scope.ServiceProvider.GetRequiredService<IVarInspector>()
                .Inspect(Path.Combine(targetDir.Path, "Gbk.Pack.1.var"));
            Assert.True(insp.IsSuccess);
            Assert.Equal(0, EncodingHealthEngine.CountLegacyEntries(insp.Value.Entries)); // fixed to Unicode
        }

        // ── result tallies ───────────────────────────────────────────────────────────────────────────────
        Assert.True(result.Copied >= 3);      // New + KeepIncoming + Cjk
        Assert.Equal(1, result.Renamed);
        Assert.True(result.Fixed >= 1);       // Cjk fixed
        Assert.True(result.Skipped >= 1);     // Exact
        Assert.Equal(1, result.Discarded);    // broken

        // ── catalog updated + history recorded + temp cleaned ────────────────────────────────────────────
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            Assert.True(await db.PackageListItems.AnyAsync(p => p.VarName == "Fresh.Look.1"));       // indexed into catalog
            Assert.True(await db.PackageListItems.AnyAsync(p => p.VarName == "Totally.Different.1")); // renamed identity indexed

            var svc = scope.ServiceProvider.GetRequiredService<IImportService>();
            var history = await svc.HistoryAsync(10);
            Assert.Single(history);
            Assert.Equal(result.RunId, history[0].Id);
            Assert.Equal(result.Copied, history[0].Copied);
        }
        Assert.False(Directory.Exists(tempRoot)); // temp workspace cleaned
    }
}
