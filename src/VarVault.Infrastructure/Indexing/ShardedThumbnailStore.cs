using System.IO;
using Microsoft.Data.Sqlite;
using VarVault.Common;
using VarVault.Domain.Indexing;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Sharded packed thumbnail store — the scalable form of the single-file store. Blobs are spread across
/// <see cref="ShardCount"/> SQLite shard files (<c>thumb_00.db … thumb_ff.db</c>) in one directory, keyed by
/// <c>PackageId</c>. At 700k-item / tens-of-GB scale this beats both a directory of loose files (NTFS MFT bloat +
/// per-cluster slack + slow enumeration) <b>and</b> a single monolithic DB:
/// <list type="bullet">
///   <item>each shard stays ~1/N of the total → per-shard <c>VACUUM</c>/checkpoint/page-cache stay cheap;</item>
///   <item>different PackageIds hit different shards → concurrent writes (N independent write locks, not one);</item>
///   <item>a corrupt shard loses only 1/N of a <b>regenerable</b> cache, not the whole thing.</item>
/// </list>
/// Routing is the low byte of PackageId (EF auto-increment ids distribute evenly across 256 shards).
/// Shard files are created lazily on first write, so an empty library keeps zero shard files. (Data-arch §5.8/D2.)
/// </summary>
public sealed class ShardedThumbnailStore : IThumbnailStore
{
    /// <summary>Number of shards. 256 keeps each shard modest even at multi-million scale (~20k rows/shard at 5M).</summary>
    public const int ShardCount = 256;

    private readonly string _dir;
    private readonly string[] _connStrings = new string[ShardCount];
    private readonly object[] _schemaLocks = new object[ShardCount];
    private readonly bool[] _schemaReady = new bool[ShardCount];

    public ShardedThumbnailStore(string directory)
    {
        Guard.NotNullOrWhiteSpace(directory);
        _dir = Path.GetFullPath(directory);
        Directory.CreateDirectory(_dir);
        for (var i = 0; i < ShardCount; i++)
        {
            _connStrings[i] = $"Data Source={ShardFile(i)}";
            _schemaLocks[i] = new object();
        }

        // Self-migrate a legacy single-file cache (the sibling `thumbnails.db`) into the shards, once, in the
        // background — best-effort, non-blocking, and a no-op if it isn't there. Keeps the whole concern in
        // Infrastructure (no startup wiring / cross-layer reference needed). The cache is regenerable regardless.
        var legacy = LegacyDbPath;
        if (legacy is not null && File.Exists(legacy))
            _ = Task.Run(async () => { try { await MigrateLegacyAsync(legacy).ConfigureAwait(false); } catch { /* best-effort */ } });
    }

    /// <summary>The legacy single-file cache path: <c>thumbnails.db</c> beside the shard directory (or null).</summary>
    private string? LegacyDbPath
    {
        get
        {
            var parent = Path.GetDirectoryName(_dir.TrimEnd(Path.DirectorySeparatorChar));
            return parent is null ? null : Path.Combine(parent, "thumbnails.db");
        }
    }

    private static int ShardOf(long packageId) => (int)((ulong)packageId & 0xFF);
    private string ShardFile(int shard) => Path.Combine(_dir, $"thumb_{shard:x2}.db");

    public async Task PutAsync(long packageId, byte[] jpeg, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(jpeg);
        var shard = ShardOf(packageId);
        EnsureSchema(shard);
        await using var connection = await OpenAsync(shard, cancellationToken).ConfigureAwait(false);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Thumb(PackageId, Bytes) VALUES ($id, $bytes) " +
                          "ON CONFLICT(PackageId) DO UPDATE SET Bytes = excluded.Bytes;";
        cmd.Parameters.AddWithValue("$id", packageId);
        cmd.Parameters.AddWithValue("$bytes", jpeg);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetAsync(long packageId, CancellationToken cancellationToken = default)
    {
        var shard = ShardOf(packageId);
        if (!File.Exists(ShardFile(shard)))
            return null; // shard never written → no thumbnail, no need to create it
        EnsureSchema(shard);
        await using var connection = await OpenAsync(shard, cancellationToken).ConfigureAwait(false);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Bytes FROM Thumb WHERE PackageId = $id;";
        cmd.Parameters.AddWithValue("$id", packageId);
        return await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as byte[];
    }

    public async Task<bool> ExistsAsync(long packageId, CancellationToken cancellationToken = default)
    {
        var shard = ShardOf(packageId);
        if (!File.Exists(ShardFile(shard)))
            return false;
        EnsureSchema(shard);
        await using var connection = await OpenAsync(shard, cancellationToken).ConfigureAwait(false);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Thumb WHERE PackageId = $id;";
        cmd.Parameters.AddWithValue("$id", packageId);
        return await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>
    /// One-time migration from a legacy single-file <c>thumbnails.db</c> into the shards, then delete the legacy
    /// file (+ its WAL/SHM). No-op if the legacy file is absent, so it's safe to call on every launch. Preserves
    /// the existing (expensive-to-rebuild) cache instead of re-extracting over the whole library. Returns rows moved.
    /// </summary>
    public async Task<int> MigrateLegacyAsync(string legacyDbPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(legacyDbPath) || !File.Exists(legacyDbPath))
            return 0;

        var moved = 0;
        await using (var src = new SqliteConnection($"Data Source={legacyDbPath};Mode=ReadOnly"))
        {
            await src.OpenAsync(cancellationToken).ConfigureAwait(false);
            var read = src.CreateCommand();
            read.CommandText = "SELECT PackageId, Bytes FROM Thumb;";
            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                await PutAsync(reader.GetInt64(0), (byte[])reader[1], cancellationToken).ConfigureAwait(false);
                moved++;
            }
        }

        // Release the read handle so the file can be deleted, then remove the legacy DB + its sidecars.
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { legacyDbPath, legacyDbPath + "-wal", legacyDbPath + "-shm" })
            try { if (File.Exists(p)) File.Delete(p); } catch (IOException) { /* best effort */ }
        return moved;
    }

    private void EnsureSchema(int shard)
    {
        if (_schemaReady[shard])
            return;
        lock (_schemaLocks[shard])
        {
            if (_schemaReady[shard])
                return;
            using var connection = new SqliteConnection(_connStrings[shard]);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; " +
                              "CREATE TABLE IF NOT EXISTS Thumb(PackageId INTEGER PRIMARY KEY, Bytes BLOB NOT NULL);";
            cmd.ExecuteNonQuery();
            _schemaReady[shard] = true;
        }
    }

    private async Task<SqliteConnection> OpenAsync(int shard, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connStrings[shard]);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
