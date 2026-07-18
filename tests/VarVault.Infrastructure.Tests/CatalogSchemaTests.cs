using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Schema-lock verification for the initial catalog migration: tables exist, the FTS5 CJK
/// tokenizer works, and the load-bearing constraints (unique keys, FK delete behaviors,
/// one-live-migration-per-file) are enforced by the database. (Checklist 0.6–0.24.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class CatalogSchemaTests
{
    // 0.7 — migrate-up on an empty DB creates the whole schema.
    [Fact]
    public void Migration_creates_all_expected_tables()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();

        var tables = TableNames(db);

        foreach (var expected in new[]
        {
            "Repository", "Package", "VarFile", "ContentItem", "PackageContentCount",
            "Dependency", "UserSave", "SaveDependency",
            "UsageEvent", "UsageStat", "MigrationJob",
            "Profile", "ActivationLink", "LoadingPreset", "PresetMember", "VarAlias",
            "Tag", "PackageTag", "Collection", "CollectionMember", "ContentItemPref",
            "PackageListItem", "TrashItem", "Setting", "PackageSearch",
        })
        {
            Assert.Contains(expected, tables);
        }
    }

    // 0.6 — WAL journal mode enabled through the EF-managed connection.
    [Fact]
    public void Connection_uses_wal_journal_mode()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();
        Assert.Equal("wal", Scalar(db, "PRAGMA journal_mode;")?.ToString()?.ToLowerInvariant());
    }

    // 0.23 🔒 — FTS5 trigram matches space-less CJK.
    [Fact]
    public void Fts5_trigram_matches_spaceless_cjk()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();

        Exec(db, "INSERT INTO PackageSearch(rowid, Blob) VALUES (1, '刘亦菲美女写真集');");
        Exec(db, "INSERT INTO PackageSearch(rowid, Blob) VALUES (2, 'unrelated english content');");

        var hits = Scalar(db, "SELECT count(*) FROM PackageSearch WHERE PackageSearch MATCH '刘亦菲';");
        Assert.Equal(1L, hits);
    }

    // 0.18 — UsageEvent timestamp stored as an integer UTC epoch, not a datetime string.
    [Fact]
    public void UsageEvent_timestamp_is_stored_as_integer_epoch()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();

        var pkg = NewPackage("Creator.Pkg.1");
        db.Packages.Add(pkg);
        db.SaveChanges();

        const long epochMs = 1_700_000_000_000;
        db.UsageEvents.Add(new UsageEvent { PackageId = pkg.Id, TimestampUnixMs = epochMs, Kind = UsageKind.Load });
        db.SaveChanges();

        Assert.Equal("integer", Scalar(db, "SELECT typeof(TimestampUnixMs) FROM UsageEvent LIMIT 1;")?.ToString());
        Assert.Equal(epochMs, Scalar(db, "SELECT TimestampUnixMs FROM UsageEvent LIMIT 1;"));
    }

    // 0.12 — Repository round-trip with tier/capacity/serial fields.
    [Fact]
    public void Repository_round_trips_all_fields()
    {
        using var fx = new SqliteTestDatabase();
        var id = Guid.NewGuid();
        using (var db = fx.NewContext())
        {
            db.Repositories.Add(new Repository
            {
                Id = id, Name = "SSD-1", MountPath = @"E:\Repo", VolumeSerial = "ABCD-1234",
                MediaType = MediaType.Nvme, Tier = 1, PriorityInTier = 2,
                CapacityBytes = 7_000_000_000_000, FreeBytes = 1_000_000, MinFreeBytes = 50_000_000_000,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            db.SaveChanges();
        }

        using var db2 = fx.NewContext();
        var repo = db2.Repositories.Single();
        Assert.Equal("ABCD-1234", repo.VolumeSerial);
        Assert.Equal(MediaType.Nvme, repo.MediaType);
        Assert.Equal(1, repo.Tier);
        Assert.Equal(7_000_000_000_000, repo.CapacityBytes);
    }

    // 0.8 — FK enforcement: a VarFile referencing a nonexistent Repository is rejected.
    [Fact]
    public void Foreign_key_violation_is_rejected()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();

        db.VarFiles.Add(new VarFile { RepositoryId = Guid.NewGuid(), RelativePath = "orphan.var" });
        Assert.ThrowsAny<DbUpdateException>(() => db.SaveChanges());
    }

    // 0.13 🔒 — deleting the canonical VarFile nulls the pointer and does NOT delete the Package.
    [Fact]
    public void Deleting_canonical_varfile_nulls_pointer_not_package()
    {
        using var fx = new SqliteTestDatabase();
        var repoId = Guid.NewGuid();
        long pkgId, vfId;

        using (var db = fx.NewContext())
        {
            db.Repositories.Add(NewRepo(repoId));
            var pkg = NewPackage("Creator.Pkg.1");
            db.Packages.Add(pkg);
            db.SaveChanges();

            var vf = new VarFile { PackageId = pkg.Id, RepositoryId = repoId, RelativePath = "a.var" };
            db.VarFiles.Add(vf);
            db.SaveChanges();

            pkg.CanonicalVarFileId = vf.Id;
            db.SaveChanges();
            pkgId = pkg.Id;
            vfId = vf.Id;
        }

        using (var db = fx.NewContext())
        {
            var vf = db.VarFiles.Single(v => v.Id == vfId);
            db.VarFiles.Remove(vf);
            db.SaveChanges();
        }

        using (var db = fx.NewContext())
        {
            var pkg = db.Packages.SingleOrDefault(p => p.Id == pkgId);
            Assert.NotNull(pkg); // Package survives
            Assert.Null(pkg!.CanonicalVarFileId); // pointer nulled by ON DELETE SET NULL
        }
    }

    // 0.13 — IdentityKey is unique.
    [Fact]
    public void Duplicate_identity_key_is_rejected()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();

        db.Packages.Add(NewPackage("Creator.Pkg.1"));
        db.SaveChanges();
        db.Packages.Add(NewPackage("Creator.Pkg.1")); // same VarName+IdentityKey
        Assert.ThrowsAny<DbUpdateException>(() => db.SaveChanges());
    }

    // 0.14 — (RepositoryId, RelativePath) is unique.
    [Fact]
    public void Duplicate_repo_relativepath_is_rejected()
    {
        using var fx = new SqliteTestDatabase();
        var repoId = Guid.NewGuid();
        using var db = fx.NewContext();
        db.Repositories.Add(NewRepo(repoId));
        db.VarFiles.Add(new VarFile { RepositoryId = repoId, RelativePath = "dup.var" });
        db.SaveChanges();
        db.VarFiles.Add(new VarFile { RepositoryId = repoId, RelativePath = "dup.var" });
        Assert.ThrowsAny<DbUpdateException>(() => db.SaveChanges());
    }

    // 0.16 — (VarFileId, DependsOnRefKey) is unique.
    [Fact]
    public void Duplicate_dependency_edge_is_rejected()
    {
        using var fx = new SqliteTestDatabase();
        var repoId = Guid.NewGuid();
        using var db = fx.NewContext();
        db.Repositories.Add(NewRepo(repoId));
        var vf = new VarFile { RepositoryId = repoId, RelativePath = "dep.var" };
        db.VarFiles.Add(vf);
        db.SaveChanges();

        db.Dependencies.Add(new Dependency { VarFileId = vf.Id, DependsOnRefKey = "OTHER.PKG.1", DependsOnRefRaw = "Other.Pkg.1" });
        db.SaveChanges();
        db.Dependencies.Add(new Dependency { VarFileId = vf.Id, DependsOnRefKey = "OTHER.PKG.1", DependsOnRefRaw = "Other.Pkg.1" });
        Assert.ThrowsAny<DbUpdateException>(() => db.SaveChanges());
    }

    // 0.19 ⚠ — only one live MigrationJob per file; a terminal job doesn't block a new one.
    [Fact]
    public void Only_one_live_migration_job_per_file()
    {
        using var fx = new SqliteTestDatabase();
        var repoId = Guid.NewGuid();
        using var db = fx.NewContext();
        db.Repositories.Add(NewRepo(repoId));
        var vf = new VarFile { RepositoryId = repoId, RelativePath = "mig.var" };
        db.VarFiles.Add(vf);
        db.SaveChanges();

        db.MigrationJobs.Add(new MigrationJob { VarFileId = vf.Id, State = MigrationState.Copying });
        db.SaveChanges();

        db.MigrationJobs.Add(new MigrationJob { VarFileId = vf.Id, State = MigrationState.Planned });
        Assert.ThrowsAny<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Terminal_migration_job_does_not_block_a_new_live_job()
    {
        using var fx = new SqliteTestDatabase();
        var repoId = Guid.NewGuid();
        using var db = fx.NewContext();
        db.Repositories.Add(NewRepo(repoId));
        var vf = new VarFile { RepositoryId = repoId, RelativePath = "mig2.var" };
        db.VarFiles.Add(vf);
        db.SaveChanges();

        db.MigrationJobs.Add(new MigrationJob { VarFileId = vf.Id, State = MigrationState.Done });
        db.SaveChanges();
        db.MigrationJobs.Add(new MigrationJob { VarFileId = vf.Id, State = MigrationState.Planned });
        db.SaveChanges(); // allowed — the previous one is terminal

        Assert.Equal(2, db.MigrationJobs.Count(j => j.VarFileId == vf.Id));
    }

    // 0.15 — ContentItem is keyed on VarFileId and cascades on VarFile delete.
    [Fact]
    public void ContentItem_cascades_with_its_varfile()
    {
        using var fx = new SqliteTestDatabase();
        var repoId = Guid.NewGuid();
        long vfId;
        using (var db = fx.NewContext())
        {
            db.Repositories.Add(NewRepo(repoId));
            var vf = new VarFile { RepositoryId = repoId, RelativePath = "content.var" };
            db.VarFiles.Add(vf);
            db.SaveChanges();
            db.ContentItems.Add(new ContentItem { VarFileId = vf.Id, Type = ContentType.Scene, EntryPath = "Saves/scene/x.json" });
            db.SaveChanges();
            vfId = vf.Id;
        }

        using (var db = fx.NewContext())
        {
            db.VarFiles.Remove(db.VarFiles.Single(v => v.Id == vfId));
            db.SaveChanges();
        }

        using (var db = fx.NewContext())
        {
            Assert.Empty(db.ContentItems.Where(c => c.VarFileId == vfId));
        }
    }

    // 0.17/0.20/0.21/0.22 — the remaining table groups accept inserts (exist + writable).
    [Fact]
    public void Remaining_entity_groups_round_trip()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();

        var save = new UserSave { Path = @"C:\Saves\scene.json", Mtime = DateTime.UtcNow, LastScannedAt = DateTime.UtcNow };
        db.UserSaves.Add(save);
        db.SaveChanges();
        db.SaveDependencies.Add(new SaveDependency { UserSaveId = save.Id, DependsOnRefKey = "A.B.1", DependsOnRefRaw = "A.B.1" });

        var profile = new Profile { Name = "Default", DirPath = @"C:\vam\prof", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Profiles.Add(profile);
        db.SaveChanges();
        var preset = new LoadingPreset { Name = "MyPreset", ProfileId = profile.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.LoadingPresets.Add(preset);
        db.SaveChanges();
        db.PresetMembers.Add(new PresetMember { PresetId = preset.Id, PackageRefKey = "A.B.1", PackageRefRaw = "A.B.1" });
        db.VarAliases.Add(new VarAlias { MissingRefKey = "X.Y.1", MissingRefRaw = "X.Y.1", ResolvedVarName = "X.Y.2", CreatedAt = DateTime.UtcNow });

        var tag = new Tag { Name = "hair", NameKey = "HAIR" };
        db.Tags.Add(tag);
        db.Collections.Add(new Collection { Name = "Favorites" });
        db.Settings.Add(new Setting { Key = "vam.path", Value = @"C:\VaM" });
        db.TrashItems.Add(new TrashItem { OriginalPath = @"E:\a.var", TrashPath = @"E:\trash\a.var", Reason = "dedup", TrashedAt = DateTime.UtcNow, Bytes = 123 });

        db.SaveChanges();

        Assert.Equal(1, db.UserSaves.Count());
        Assert.Equal(1, db.Profiles.Count());
        Assert.Equal(1, db.PresetMembers.Count());
        Assert.Equal(1, db.VarAliases.Count());
        Assert.Equal(1, db.Settings.Count());
        Assert.Equal(1, db.TrashItems.Count());
    }

    // ---- helpers ----

    private static Repository NewRepo(Guid id) => new()
    {
        Id = id, Name = "R", MountPath = $@"E:\{id:N}", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static Package NewPackage(string varName)
    {
        var id = Domain.ValueObjects.PackageId.TryParse(varName).Value;
        return new Package
        {
            VarName = id.VarName, IdentityKey = id.IdentityKey,
            Creator = id.Creator, PackageName = id.Package,
            VersionToken = id.VersionToken, VersionSort = long.Parse(id.VersionToken),
            FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        };
    }

    private static List<string> TableNames(VarVaultDbContext db)
    {
        var names = new List<string>();
        var conn = db.Database.GetDbConnection();
        EnsureOpen(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','view');";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    private static object? Scalar(VarVaultDbContext db, string sql)
    {
        var conn = db.Database.GetDbConnection();
        EnsureOpen(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static void Exec(VarVaultDbContext db, string sql)
    {
        var conn = db.Database.GetDbConnection();
        EnsureOpen(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void EnsureOpen(DbConnection conn)
    {
        if (conn.State != System.Data.ConnectionState.Open)
            conn.Open();
    }
}
