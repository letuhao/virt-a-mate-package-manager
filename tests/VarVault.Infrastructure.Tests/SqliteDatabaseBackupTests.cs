using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Versioned catalog backups via VACUUM INTO: a consistent snapshot that restores the data, kept to
/// the last N. (Checklist X.4.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class SqliteDatabaseBackupTests
{
    [Fact]
    public async Task Backup_produces_a_consistent_restorable_copy()
    {
        using var fx = new SqliteTestDatabase();
        using var backupDir = new TempDirectory();

        using (var db = fx.NewContext())
        {
            db.Settings.Add(new Setting { Key = "vam.path", Value = @"C:\VaM" });
            await db.SaveChangesAsync();

            var backup = new SqliteDatabaseBackup(db, new FakeClock());
            var result = await backup.BackupAsync(backupDir.Path);
            Assert.True(result.IsSuccess, result.Error.ToString());
            Assert.True(File.Exists(result.Value.Path));
            Assert.True(result.Value.Bytes > 0);
        }

        // Open the backup as its own database — the row is present.
        var backupFile = Directory.GetFiles(backupDir.Path, "catalog-*.db").Single();
        var options = new DbContextOptionsBuilder<VarVaultDbContext>().UseSqlite($"Data Source={backupFile}").Options;
        using var restored = new VarVaultDbContext(options);
        Assert.Equal(@"C:\VaM", (await restored.Settings.FirstAsync(s => s.Key == "vam.path")).Value);
    }

    [Fact]
    public async Task Keeps_only_the_last_n_backups()
    {
        using var fx = new SqliteTestDatabase();
        using var backupDir = new TempDirectory();
        var clock = new FakeClock();

        using var db = fx.NewContext();
        var backup = new SqliteDatabaseBackup(db, clock);
        for (var i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1)); // distinct timestamps
            await backup.BackupAsync(backupDir.Path, keepLast: 3);
        }

        Assert.Equal(3, backup.ListBackups(backupDir.Path).Count);
    }
}
