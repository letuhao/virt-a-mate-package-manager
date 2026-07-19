using System.IO;
using Microsoft.Data.Sqlite;
using VarVault.Common;
using VarVault.Domain.Indexing;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Packed thumbnail store: a dedicated <c>thumbs.db</c> SQLite file with one blob row per PackageId —
/// so 700k thumbnails are a single DB, not loose files. (Data-arch §5.8/D2; checklist 1.33.)
/// </summary>
public sealed class SqliteThumbnailStore : IThumbnailStore
{
    private readonly string _connectionString;

    public SqliteThumbnailStore(string databasePath)
    {
        Guard.NotNullOrWhiteSpace(databasePath);
        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        _connectionString = $"Data Source={databasePath}";
        EnsureSchema();
    }

    public async Task PutAsync(long packageId, byte[] jpeg, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(jpeg);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Thumb(PackageId, Bytes) VALUES ($id, $bytes) " +
                          "ON CONFLICT(PackageId) DO UPDATE SET Bytes = excluded.Bytes;";
        cmd.Parameters.AddWithValue("$id", packageId);
        cmd.Parameters.AddWithValue("$bytes", jpeg);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetAsync(long packageId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Bytes FROM Thumb WHERE PackageId = $id;";
        cmd.Parameters.AddWithValue("$id", packageId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result as byte[];
    }

    public async Task<bool> ExistsAsync(long packageId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Thumb WHERE PackageId = $id;";
        cmd.Parameters.AddWithValue("$id", packageId);
        return await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS Thumb(PackageId INTEGER PRIMARY KEY, Bytes BLOB NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
