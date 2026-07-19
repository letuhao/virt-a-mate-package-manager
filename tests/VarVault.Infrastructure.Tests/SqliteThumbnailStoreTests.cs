using System.IO;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>Thumbnails live in one packed SQLite store keyed by PackageId, not loose files. (1.33.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class SqliteThumbnailStoreTests
{
    [Fact]
    public async Task Put_get_and_overwrite_by_package_id()
    {
        using var dir = new TempDirectory();
        var store = new SqliteThumbnailStore(dir.File("thumbs.db"));

        var a = new byte[] { 1, 2, 3 };
        await store.PutAsync(42, a);
        Assert.Equal(a, await store.GetAsync(42));
        Assert.True(await store.ExistsAsync(42));

        var b = new byte[] { 9, 8, 7, 6 };
        await store.PutAsync(42, b); // overwrite
        Assert.Equal(b, await store.GetAsync(42));
    }

    [Fact]
    public async Task Missing_thumbnail_is_null()
    {
        using var dir = new TempDirectory();
        var store = new SqliteThumbnailStore(dir.File("thumbs.db"));
        Assert.Null(await store.GetAsync(999));
        Assert.False(await store.ExistsAsync(999));
    }

    [Fact]
    public async Task Many_thumbnails_are_one_packed_file_not_loose_files()
    {
        using var dir = new TempDirectory();
        var dbPath = dir.File("thumbs.db");
        var store = new SqliteThumbnailStore(dbPath);
        for (var i = 0; i < 50; i++)
            await store.PutAsync(i, new byte[] { (byte)i });

        // The store is a single DB file (+ WAL/shm), never 50 loose files.
        Assert.True(File.Exists(dbPath));
        var looseImages = Directory.GetFiles(dir.Path, "*.jpg");
        Assert.Empty(looseImages);
    }
}
