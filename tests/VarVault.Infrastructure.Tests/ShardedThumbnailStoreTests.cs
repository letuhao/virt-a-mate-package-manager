using System.IO;
using System.Linq;
using System.Text;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// The sharded thumbnail store spreads blobs across many small SQLite files (scales to 700k items / tens of GB),
/// round-trips by PackageId, and migrates a legacy single-file cache into the shards.
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class ShardedThumbnailStoreTests
{
    private static byte[] Blob(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task Round_trips_across_shards()
    {
        using var dir = new TempDirectory();
        var store = new ShardedThumbnailStore(Path.Combine(dir.Path, "thumbnails"));

        // ids 1 and 2 land in different shards; 257 shares shard 1 with id 1 (routing = id & 0xFF).
        await store.PutAsync(1, Blob("one"));
        await store.PutAsync(2, Blob("two"));
        await store.PutAsync(257, Blob("two-fifty-seven"));

        Assert.Equal("one", Encoding.UTF8.GetString((await store.GetAsync(1))!));
        Assert.Equal("two", Encoding.UTF8.GetString((await store.GetAsync(2))!));
        Assert.Equal("two-fifty-seven", Encoding.UTF8.GetString((await store.GetAsync(257))!));

        Assert.True(await store.ExistsAsync(1));
        Assert.False(await store.ExistsAsync(999));           // never written
        Assert.Null(await store.GetAsync(999));

        // Blobs actually landed in separate shard files (thumb_01.db + thumb_02.db).
        var shardFiles = Directory.GetFiles(Path.Combine(dir.Path, "thumbnails"), "thumb_*.db");
        Assert.True(shardFiles.Length >= 2, $"expected ≥2 shard files, got {shardFiles.Length}");
        Assert.Contains(shardFiles, f => f.EndsWith("thumb_01.db"));
        Assert.Contains(shardFiles, f => f.EndsWith("thumb_02.db"));
    }

    [Fact]
    public async Task Upsert_overwrites_same_id()
    {
        using var dir = new TempDirectory();
        var store = new ShardedThumbnailStore(Path.Combine(dir.Path, "thumbnails"));
        await store.PutAsync(42, Blob("first"));
        await store.PutAsync(42, Blob("second"));
        Assert.Equal("second", Encoding.UTF8.GetString((await store.GetAsync(42))!));
    }

    [Fact]
    public async Task Migrates_legacy_single_file_into_shards_then_deletes_it()
    {
        using var dir = new TempDirectory();
        var legacyPath = Path.Combine(dir.Path, "old", "thumbnails.db");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);

        // Seed a legacy single-file cache.
        var legacy = new SqliteThumbnailStore(legacyPath);
        await legacy.PutAsync(1, Blob("a"));
        await legacy.PutAsync(2, Blob("b"));
        await legacy.PutAsync(500, Blob("c"));
        Assert.True(File.Exists(legacyPath));

        var store = new ShardedThumbnailStore(Path.Combine(dir.Path, "thumbnails"));
        var moved = await store.MigrateLegacyAsync(legacyPath);

        Assert.Equal(3, moved);
        Assert.Equal("a", Encoding.UTF8.GetString((await store.GetAsync(1))!));
        Assert.Equal("b", Encoding.UTF8.GetString((await store.GetAsync(2))!));
        Assert.Equal("c", Encoding.UTF8.GetString((await store.GetAsync(500))!));
        Assert.False(File.Exists(legacyPath));                // legacy file removed after migration
    }

    [Fact]
    public async Task Migrate_is_noop_when_no_legacy_file()
    {
        using var dir = new TempDirectory();
        var store = new ShardedThumbnailStore(Path.Combine(dir.Path, "thumbnails"));
        Assert.Equal(0, await store.MigrateLegacyAsync(Path.Combine(dir.Path, "does-not-exist.db")));
    }
}
