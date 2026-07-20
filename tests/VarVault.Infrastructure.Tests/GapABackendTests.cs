using System.IO;
using System.IO.Compression;
using System.Text;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Library;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// 24-checklist A4/A9 · Near-duplicate grouping (same payload, different identity) and missing-meta surfacing —
/// thin catalog queries over already-stored columns. Seeded SQLite; deterministic.
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class GapABackendTests
{
    private static readonly Guid R = Guid.NewGuid();

    [Fact]
    public async Task Near_dup_groups_share_payload_across_distinct_identities()
    {
        using var fx = new SqliteTestDatabase();
        using (var db = fx.NewContext())
        {
            AddRepo(db);
            // Two DIFFERENT identities, SAME payload signature → one near-dup group of two.
            AddPkg(db, 1, "Alice.Scene.1");
            AddPkg(db, 2, "Bob.Scene.1");
            AddVar(db, 10, 1, "a.var", payload: "PAY-SAME");
            AddVar(db, 11, 2, "b.var", payload: "PAY-SAME");
            // Unique payload → not a near-dup.
            AddPkg(db, 3, "Carol.Scene.1");
            AddVar(db, 12, 3, "c.var", payload: "PAY-OTHER");
            // Same identity + same payload → an EXACT dup, not counted as near (needs ≥2 distinct identities).
            AddVar(db, 13, 1, "a2.var", payload: "PAY-SAME");
            await db.SaveChangesAsync();
        }

        using var read = fx.NewContext();
        var near = await new EfReclaimService(read, null!, null!).NearDuplicateGroupsAsync();

        Assert.Single(near);
        Assert.Equal("PAY-SAME", near[0].PayloadSignature);
        Assert.Contains("ALICE.SCENE.1", near[0].Names);
        Assert.Contains("BOB.SCENE.1", near[0].Names);
        Assert.True(near[0].Members.Count >= 2);
    }

    [Fact]
    public async Task Missing_meta_lists_only_vars_flagged_missing_meta()
    {
        using var fx = new SqliteTestDatabase();
        using (var db = fx.NewContext())
        {
            AddRepo(db);
            AddPkg(db, 1, "Ok.Var.1");
            AddPkg(db, 2, "NoMeta.Var.1");
            AddVar(db, 10, 1, "ok.var", integrity: IntegrityStatus.Ok);
            AddVar(db, 11, 2, "nometa.var", integrity: IntegrityStatus.MissingMeta);
            AddVar(db, 12, 1, "corrupt.var", integrity: IntegrityStatus.CorruptZip); // not missing-meta
            await db.SaveChangesAsync();
        }

        using var read = fx.NewContext();
        var missing = await new EfHealthService(read, null!).MissingMetaAsync();

        Assert.Single(missing);
        Assert.Equal(11, missing[0].VarFileId);
    }

    [Fact]
    public async Task Intake_classifies_new_then_exact_duplicate()
    {
        using var folder = new TempDirectory();
        var varPath = Path.Combine(folder.Path, "Creator.Pkg.1.var");
        WriteVar(varPath, """{"creatorName":"Creator","packageName":"Pkg"}""", ("Custom/Scene/s.json", "{}"));

        var inspector = new VarInspector();
        var sig = inspector.Inspect(varPath).Value.Signatures!;

        using var fx = new SqliteTestDatabase();

        // Empty catalog → New.
        using (var read = fx.NewContext())
        {
            var items = await new EfIntakeService(read, inspector).ClassifyFolderAsync(folder.Path);
            Assert.Single(items);
            Assert.Equal("New", items[0].Classification);
        }

        // Seed the catalog with this var's identity + signatures → Exact duplicate.
        using (var db = fx.NewContext())
        {
            AddRepo(db);
            db.Packages.Add(new Package
            {
                Id = 1, VarName = "Creator.Pkg.1", IdentityKey = "CREATOR.PKG.1", Creator = "Creator", PackageName = "Pkg",
                VersionToken = "1", VersionSort = 1, FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
            });
            db.VarFiles.Add(new VarFile
            {
                Id = 1, PackageId = 1, RepositoryId = R, RelativePath = "Creator.Pkg.1.var", SizeBytes = 100,
                ContentSignature = sig.ContentSignature, PayloadSignature = sig.PayloadSignature,
                ContentSignatureNoPath = sig.ContentSignatureNoPath, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using (var read = fx.NewContext())
        {
            var items = await new EfIntakeService(read, inspector).ClassifyFolderAsync(folder.Path);
            Assert.Single(items);
            Assert.Equal("Exact duplicate", items[0].Classification);
        }
    }

    [Fact]
    public async Task Library_page_includes_per_content_type_counts()
    {
        using var fx = new SqliteTestDatabase();
        using (var db = fx.NewContext())
        {
            db.Packages.Add(new Package
            {
                Id = 1, VarName = "A.B.1", IdentityKey = "A.B.1", Creator = "A", PackageName = "B",
                VersionToken = "1", VersionSort = 1, FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
            });
            db.PackageListItems.Add(new PackageListItem
            {
                PackageId = 1, VarName = "A.B.1", Creator = "A", PackageName = "B", VersionToken = "1",
                PrimaryType = ContentType.Scene, TotalSize = 100, Class = ContentClass.Cold,
                OnlineInstanceCount = 1, AddedAt = DateTime.UtcNow,
            });
            db.PackageContentCounts.Add(new PackageContentCount { PackageId = 1, Type = ContentType.Scene, Count = 3 });
            db.PackageContentCounts.Add(new PackageContentCount { PackageId = 1, Type = ContentType.Plugin, Count = 2 });
            await db.SaveChangesAsync();
        }

        using var read = fx.NewContext();
        var page = await new EfLibraryQueryService(read).GetPageAsync(new LibraryQuery());

        var row = Assert.Single(page.Items);
        Assert.NotNull(row.ContentCounts);
        Assert.Equal(3, row.ContentCounts!["Scene"]);
        Assert.Equal(2, row.ContentCounts["Plugin"]);
        Assert.Contains("Sc 3", row.ContentSummary);
        Assert.Contains("Pl 2", row.ContentSummary);
    }

    private static void WriteVar(string path, string meta, params (string Name, string Content)[] entries)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "meta.json", meta);
        foreach (var (name, content) in entries)
            Add(zip, name, content);

        static void Add(ZipArchive zip, string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = e.Open();
            s.Write(Encoding.UTF8.GetBytes(content));
        }
    }

    private static void AddRepo(VarVaultDbContext db) => db.Repositories.Add(new Repository
    {
        Id = R, Name = "r", MountPath = @"X:\r", Tier = 3, IsOnline = true, IsEnabled = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    });

    private static void AddPkg(VarVaultDbContext db, long id, string name) => db.Packages.Add(new Package
    {
        Id = id, VarName = name, IdentityKey = name.ToUpperInvariant(), Creator = name.Split('.')[0], PackageName = name,
        VersionToken = "1", VersionSort = 1, FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
    });

    private static void AddVar(VarVaultDbContext db, long id, long pkgId, string rel,
        string? payload = null, IntegrityStatus integrity = IntegrityStatus.Ok) => db.VarFiles.Add(new VarFile
    {
        Id = id, PackageId = pkgId, RepositoryId = R, RelativePath = rel, SizeBytes = 100,
        PayloadSignature = payload, IntegrityStatus = integrity, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
    });
}
