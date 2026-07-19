using System.Data.Common;
using System.Globalization;
using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Common;

namespace VarVault.Infrastructure.Persistence;

/// <summary>A stored catalog backup.</summary>
public sealed record BackupInfo(string Path, DateTime CreatedAtUtc, long Bytes);

/// <summary>
/// ⚠ Versioned catalog backups: a consistent snapshot via SQLite <c>VACUUM INTO</c> (safe while the
/// DB is in use), kept to the last N. Run on a schedule AND before every destructive batch.
/// (Data-arch §5.9; checklist X.4.)
/// </summary>
public sealed class SqliteDatabaseBackup(VarVaultDbContext db, IClock clock)
{
    public async Task<Result<BackupInfo>> BackupAsync(
        string backupDirectory,
        int keepLast = 10,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(backupDirectory);
        Directory.CreateDirectory(backupDirectory);

        var stamp = clock.UtcNow.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var path = Path.Combine(backupDirectory, $"catalog-{stamp}.db");

        try
        {
            // VACUUM INTO writes a consistent copy without blocking readers; escape the literal path.
            var literal = path.Replace("'", "''", StringComparison.Ordinal);
            await db.Database.ExecuteSqlRawAsync($"VACUUM main INTO '{literal}';", cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            return Result.Failure<BackupInfo>("backup.failed", ex.Message);
        }

        Prune(backupDirectory, keepLast);
        var info = new FileInfo(path);
        return new BackupInfo(path, clock.UtcNow.UtcDateTime, info.Exists ? info.Length : 0);
    }

    public IReadOnlyList<BackupInfo> ListBackups(string backupDirectory)
    {
        Guard.NotNullOrWhiteSpace(backupDirectory);
        if (!Directory.Exists(backupDirectory))
            return [];

        return Directory.EnumerateFiles(backupDirectory, "catalog-*.db")
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupInfo(f.FullName, f.LastWriteTimeUtc, f.Length))
            .ToList();
    }

    private static void Prune(string backupDirectory, int keepLast)
    {
        if (keepLast <= 0)
            return;
        var files = Directory.EnumerateFiles(backupDirectory, "catalog-*.db")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(keepLast)
            .ToList();
        foreach (var stale in files)
        {
            try { File.Delete(stale); }
            catch (IOException) { /* best effort */ }
        }
    }
}
