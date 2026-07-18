using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VarVault.Infrastructure.Persistence;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// DB-safety startup guards: the catalog refuses unsafe locations (0.10 ⚠) and detects
/// corruption via integrity check (0.9).
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class CatalogSafetyTests
{
    // 0.10 ⚠ — removable/network volume rejected.
    [Theory]
    [InlineData(DriveType.Removable)]
    [InlineData(DriveType.Network)]
    [InlineData(DriveType.CDRom)]
    public void Rejects_db_on_non_fixed_volume(DriveType driveType)
    {
        var result = CatalogLocationGuard.Validate(
            @"R:\catalog.db", repositoryPaths: [], driveTypeResolver: _ => driveType);

        Assert.True(result.IsFailure);
        Assert.Equal("catalog.location.volume", result.Error.Code);
    }

    [Fact]
    public void Accepts_db_on_fixed_volume_outside_repositories()
    {
        var result = CatalogLocationGuard.Validate(
            @"C:\Users\me\AppData\VarVault\catalog.db",
            repositoryPaths: [@"E:\Repo", @"F:\Repo2"],
            driveTypeResolver: _ => DriveType.Fixed);

        Assert.True(result.IsSuccess);
    }

    // 0.10 ⚠ — DB inside a registered repository rejected.
    [Fact]
    public void Rejects_db_nested_within_a_repository()
    {
        var result = CatalogLocationGuard.Validate(
            @"E:\Repo\sub\catalog.db",
            repositoryPaths: [@"E:\Repo"],
            driveTypeResolver: _ => DriveType.Fixed);

        Assert.True(result.IsFailure);
        Assert.Equal("catalog.location.repo", result.Error.Code);
    }

    [Fact]
    public void Rejects_repository_nested_within_the_db_directory()
    {
        // The reverse containment is also unsafe (a repo living under the DB folder).
        var result = CatalogLocationGuard.Validate(
            @"E:\Data\catalog.db",
            repositoryPaths: [@"E:\Data\Repo"],
            driveTypeResolver: _ => DriveType.Fixed);

        Assert.True(result.IsFailure);
        Assert.Equal("catalog.location.repo", result.Error.Code);
    }

    // 0.9 — integrity check passes on a healthy migrated DB.
    [Fact]
    public void Integrity_check_passes_on_healthy_database()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();

        var result = CatalogIntegrity.Check(db.Database.GetDbConnection());

        Assert.True(result.IsSuccess);
    }

    // 0.9 — a corrupt database is detected and surfaced (not opened silently).
    [Fact]
    public void Integrity_check_detects_a_corrupt_database()
    {
        using var fx = new SqliteTestDatabase();

        // Create a valid DB, then corrupt the file's SQLite header so it is no longer a valid image.
        using (var db = fx.NewContext())
        {
            _ = db.Database.GetDbConnection(); // materialize + migrate
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        CorruptHeader(fx.Path);

        using var corrupt = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fx.Path}");
        var result = CatalogIntegrity.Check(corrupt);

        Assert.True(result.IsFailure);
        Assert.Equal("catalog.integrity", result.Error.Code);
    }

    // 0.9/0.10 — the initializer orchestrates guard → migrate → integrity for a fresh DB.
    [Fact]
    public async Task Initializer_prepares_a_fresh_database()
    {
        using var dir = new TempDirectory();
        var dbPath = dir.File("catalog.db");
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<VarVaultDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;

        using var db = new VarVaultDbContext(options);
        var initializer = new CatalogDatabaseInitializer(NullLogger<CatalogDatabaseInitializer>.Instance);

        var result = await initializer.PrepareAsync(
            db, dbPath, repositoryPaths: [], driveTypeResolver: _ => DriveType.Fixed);

        Assert.True(result.IsSuccess);
        Assert.True(db.Database.CanConnect());
    }

    private static void CorruptHeader(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        // Overwrite the 16-byte "SQLite format 3\0" magic with garbage.
        fs.Seek(0, SeekOrigin.Begin);
        fs.Write(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xDE, 0xAD, 0xBE, 0xEF, 0xDE, 0xAD, 0xBE, 0xEF, 0xDE, 0xAD, 0xBE, 0xEF });
        fs.Flush();
    }
}
