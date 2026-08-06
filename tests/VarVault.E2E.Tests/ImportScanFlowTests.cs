using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Import;
using VarVault.Infrastructure.Library;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Import;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Repositories;
using VarVault.Sdk.Settings;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// Slice A proof (doc 31 Phase 4.1): the import scan classifies incoming vars against the indexed catalog into the
/// six lanes, recommends a resolution for conflicts, and builds the per-entry diff. A deterministic synthetic case
/// covers every lane (incl. a crafted legacy-CJK var); a second, env-gated case proves it over the real fixture folder.
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ImportScanFlowTests
{
    [Fact]
    public async Task Scan_classifies_every_lane_over_an_indexed_catalog()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var importDir = new TempDirectory();

        // ── catalog (indexed): two known packages ──────────────────────────────────────────────────────
        WriteVar(repoDir.Path, "Creator.PackA.1.var", "Creator", "PackA",
            [("Custom/a1.vam", "AAA"), ("Custom/a2.vam", "BBB")]);
        WriteVar(repoDir.Path, "Creator.PackB.1.var", "Creator", "PackB",
            [("Custom/b1.vam", "CCC")]);

        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("catalog", repoDir.Path));
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        // ── import folder (multi-level, one per lane) ───────────────────────────────────────────────────
        var sub = Directory.CreateDirectory(Path.Combine(importDir.Path, "downloads", "batch")).FullName;
        WriteVar(sub, "Creator.PackA.1.var", "Creator", "PackA", [("Custom/a1.vam", "AAA"), ("Custom/a2.vam", "BBB")]); // Exact (identical)
        WriteVar(sub, "Creator.PackB.1.var", "Creator", "PackB", [("Custom/b1.vam", "CCC"), ("Custom/b2.vam", "EXTRA")]); // Conflict (same id, +entry)
        WriteVar(sub, "Fresh.Look.1.var", "Fresh", "Look", [("Custom/new.vam", "NEW")]);                                  // New
        WriteVar(sub, "Wrong.Name.1.var", "Totally", "Different", [("Custom/x.vam", "X")]);                               // Naming (meta != filename, not in repo)
        WriteBadZip(Path.Combine(sub, "broken.var"));                                                                     // Corrupt
        WriteGbkVar(Path.Combine(sub, "Gbk.Pack.1.var"), "Gbk", "Pack");                                                  // Cjk (valid, legacy entry name)
        // Intra-batch dup: the SAME content in two folders — one New, the other auto-skip. (4.2)
        var sub2 = Directory.CreateDirectory(Path.Combine(importDir.Path, "downloads", "batch2")).FullName;
        WriteVar(sub, "Dup.Batch.1.var", "Dup", "Batch", [("Custom/d.vam", "DUP")]);
        WriteVar(sub2, "Dup.Batch.1.var", "Dup", "Batch", [("Custom/d.vam", "DUP")]);

        // ── scan ────────────────────────────────────────────────────────────────────────────────────────
        using var read = host.Host.Services.CreateScope();
        var svc = read.ServiceProvider.GetRequiredService<IImportService>();
        var session = await svc.ScanAsync(new ImportSpec([importDir.Path], Guid.NewGuid()), progress: null!);

        ImportItem Item(string file) => session.Items.Single(i => i.FileName == file);
        Assert.Equal(ImportLane.Exact, Item("Creator.PackA.1.var").Lane);
        Assert.Equal(ImportLane.New, Item("Fresh.Look.1.var").Lane);
        Assert.Equal(ImportLane.Naming, Item("Wrong.Name.1.var").Lane);
        Assert.Equal(ImportLane.Corrupt, Item("broken.var").Lane);
        Assert.Equal(ImportLane.Cjk, Item("Gbk.Pack.1.var").Lane);

        var conflict = Item("Creator.PackB.1.var");
        Assert.Equal(ImportLane.Conflict, conflict.Lane);
        Assert.NotNull(conflict.Existing);                               // matched the repo copy
        Assert.NotEmpty(conflict.Diff);                                  // per-entry diff built
        Assert.Contains(conflict.Diff, d => d.Kind == '+');             // the extra entry shows as added
        Assert.Equal(ImportDecision.KeepIncoming, conflict.Recommendation); // incoming has more entries → keep incoming
        Assert.Equal(ImportDecision.None, conflict.Decision);           // review lane starts undecided

        // auto lanes pre-decided; CJK auto-fixes.
        Assert.Equal(ImportDecision.Import, Item("Fresh.Look.1.var").Decision);
        Assert.Equal(ImportDecision.Skip, Item("Creator.PackA.1.var").Decision);
        Assert.Equal(ImportDecision.ImportAndFix, Item("Gbk.Pack.1.var").Decision);
        Assert.True(Item("Gbk.Pack.1.var").Signals.GbkEntryCount >= 1);

        // intra-batch dedup: two identical Dup.Batch.1.var → exactly one New, one auto-skip. (4.2)
        var dups = session.Items.Where(i => i.FileName == "Dup.Batch.1.var").ToList();
        Assert.Equal(2, dups.Count);
        Assert.Equal(1, dups.Count(d => d.Lane == ImportLane.New));
        Assert.Equal(1, dups.Count(d => d.Lane == ImportLane.Exact && d.Decision == ImportDecision.Skip));
    }

    [SkippableFact]
    public async Task Scan_over_the_real_import_fixture()
    {
        var corpus = TestCorpus.Primary;
        var fixture = Environment.GetEnvironmentVariable("VARVAULT_TEST_IMPORT");
        Skip.If(corpus is null, "no VARVAULT_TEST_CORPUS configured");
        Skip.If(string.IsNullOrWhiteSpace(fixture) || !Directory.Exists(fixture), "no VARVAULT_TEST_IMPORT fixture folder");

        await using var host = TestHost.Create(withPersistence: true);
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("real", corpus!));
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        using var read = host.Host.Services.CreateScope();
        var svc = read.ServiceProvider.GetRequiredService<IImportService>();
        var session = await svc.ScanAsync(new ImportSpec([fixture!], Guid.NewGuid()), progress: null!);

        ImportItem? Find(string file) => session.Items.FirstOrDefault(i => i.FileName == file);
        // Lanes we can assert on the real fixture (CJK there is an exact copy → Exact; unit-tested separately).
        Assert.Equal(ImportLane.Exact, Find("BaGe.VAM_BaGe_Shoe41.1.var")!.Lane);
        Assert.Equal(ImportLane.Conflict, Find("Barbarossa.Tatsumaki.2.var")!.Lane);
        Assert.Equal(ImportLane.New, Find("ImportTest.FreshLook.1.var")!.Lane);
        Assert.Equal(ImportLane.Naming, Find("SomeGuy.RenamedByMistake.1.var")!.Lane);
        Assert.Equal(ImportLane.Corrupt, Find("truncated.var")!.Lane);
        Assert.Equal(ImportLane.Corrupt, Find("no_meta.var")!.Lane);

        // archives are extracted (their vars appear as items) and password/broken archives are failed sources. (Slice B)
        Assert.Contains(session.Sources, s => s.Kind == ImportSourceKind.Archive && s.Status == ImportSourceStatus.Ok && s.VarCount > 0);
        Assert.Contains(session.Sources, s => s.Status == ImportSourceStatus.PasswordProtected);   // secret_premium.zip
        Assert.Contains(session.Sources, s => s.Status == ImportSourceStatus.CorruptArchive);      // broken_archive.7z
        // a var that only exists inside an archive made it into the scan (looks_pack.zip / mixed_pack.7z contents).
        Assert.Contains(session.Items, i => i.SourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                         || i.SourcePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase));

        // clean the temp workspace the scan created.
        try { if (Directory.Exists(session.TempRoot)) Directory.Delete(session.TempRoot, true); } catch { }
    }

    [Fact]
    public async Task Scan_honors_cancellation()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var importDir = new TempDirectory();
        WriteVar(importDir.Path, "X.Y.1.var", "X", "Y", [("Custom/a.vam", "a")]);

        using var read = host.Host.Services.CreateScope();
        var svc = read.ServiceProvider.GetRequiredService<IImportService>();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            svc.ScanAsync(new ImportSpec([importDir.Path], Guid.NewGuid()), progress: null!, cts.Token));
    }

    /// <summary>
    /// Catalog ContentSignature null/stale must not force a Conflict when the on-disk copy is byte-identical:
    /// ConflictItem re-inspects live and reclassifies Exact (D1).
    /// </summary>
    [Fact]
    public async Task Identical_incoming_with_null_catalog_signature_is_Exact_not_Conflict()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var importDir = new TempDirectory();

        WriteVar(repoDir.Path, "Creator.Same.1.var", "Creator", "Same",
            [("Custom/a.vam", "AAA"), ("Custom/b.vam", "BBB")]);

        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("catalog", repoDir.Path));
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        // Simulate stale/incomplete catalog: wipe signatures so Classify cannot Exact-match via DB.
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            foreach (var vf in db.VarFiles)
            {
                vf.ContentSignature = null;
                vf.PayloadSignature = null;
                vf.ContentSignatureNoPath = null;
            }
            await db.SaveChangesAsync();
        }

        WriteVar(importDir.Path, "Creator.Same.1.var", "Creator", "Same",
            [("Custom/a.vam", "AAA"), ("Custom/b.vam", "BBB")]);

        using var read = host.Host.Services.CreateScope();
        var session = await read.ServiceProvider.GetRequiredService<IImportService>()
            .ScanAsync(new ImportSpec([importDir.Path], Guid.NewGuid()), progress: null!);

        var item = Assert.Single(session.Items);
        Assert.Equal(ImportLane.Exact, item.Lane);
        Assert.Equal(ImportDecision.Skip, item.Decision);
        Assert.NotNull(item.Existing);
        Assert.Empty(item.Diff);
    }

    // ── fixtures ─────────────────────────────────────────────────────────────────────────────────────────
    private static string Meta(string creator, string package) =>
        "{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\",\"dependencies\":{}}";

    private static void WriteVar(string dir, string fileName, string creator, string package, (string, string)[] entries)
    {
        using var fs = new FileStream(Path.Combine(dir, fileName), FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "meta.json", Meta(creator, package));
        foreach (var (name, content) in entries)
            Add(zip, name, content);
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }

    private static void WriteBadZip(string path) => File.WriteAllText(path, "this is not a zip archive at all\n");

    /// <summary>Writes a valid var whose one entry name is legacy GBK bytes with no UTF-8 flag (VaM-unloadable).
    /// Trick: create an ASCII placeholder of the same byte length, then splice the GBK bytes in place.</summary>
    private static void WriteGbkVar(string path, string creator, string package)
    {
        // GBK bytes for 衣裙鞋 (3 chars × 2 bytes) — replaces the 6-byte ASCII placeholder "ZZZZZZ".
        byte[] gbk = [0xD2, 0xC2, 0xC8, 0xB9, 0xD0, 0xAC];
        byte[] placeholder = Encoding.ASCII.GetBytes("ZZZZZZ");

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            Add(zip, "meta.json", Meta(creator, package));
            Add(zip, "Custom/ZZZZZZ.vam", "cjk placeholder"); // ASCII name → no UTF-8 flag set
        }

        var bytes = File.ReadAllBytes(path);
        for (var i = 0; i + placeholder.Length <= bytes.Length; i++)
        {
            var match = true;
            for (var j = 0; j < placeholder.Length; j++)
                if (bytes[i + j] != placeholder[j]) { match = false; break; }
            if (match) { Array.Copy(gbk, 0, bytes, i, gbk.Length); i += placeholder.Length - 1; }
        }
        File.WriteAllBytes(path, bytes);
    }
}
