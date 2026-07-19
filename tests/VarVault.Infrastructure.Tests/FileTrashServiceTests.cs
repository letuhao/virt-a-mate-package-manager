using System.IO;
using VarVault.Infrastructure.Safety;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Trash safety: deletes route to trash (move, not hard-delete) with a per-item manifest so restore
/// works even without the catalog DB. (Checklist X.1/X.2/X.3.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class FileTrashServiceTests
{
    private static FileTrashService NewService(TempDirectory trash) =>
        new(Path.Combine(trash.Path, "trash"), new FakeClock());

    [Fact]
    public async Task Trashing_moves_the_file_and_writes_a_manifest()
    {
        using var dir = new TempDirectory();
        var file = dir.File("victim.var");
        await File.WriteAllTextAsync(file, "payload");
        var trash = NewService(dir);

        var result = await trash.TrashAsync(file, "dedup");

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(file));                 // moved, not left
        Assert.True(File.Exists(result.Value.TrashPath)); // now in trash
        Assert.Equal("dedup", result.Value.Reason);
        Assert.Equal(Path.GetFullPath(file), result.Value.OriginalPath);
    }

    [Fact]
    public async Task Restore_returns_the_file_to_its_original_path()
    {
        using var dir = new TempDirectory();
        var file = dir.File("victim.var");
        await File.WriteAllTextAsync(file, "payload");
        var trash = NewService(dir);

        var entry = (await trash.TrashAsync(file, "test")).Value;
        var restore = await trash.RestoreAsync(entry.Id);

        Assert.True(restore.IsSuccess);
        Assert.True(File.Exists(file));
        Assert.Equal("payload", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task Restore_works_from_the_manifest_alone_without_a_database()
    {
        using var dir = new TempDirectory();
        var file = dir.File("victim.var");
        await File.WriteAllTextAsync(file, "payload");
        var trash = NewService(dir);
        var entry = (await trash.TrashAsync(file, "test")).Value;

        // Simulate DB loss: a brand-new service instance with only the trash folder can still restore.
        var freshService = new FileTrashService(Path.Combine(dir.Path, "trash"), new FakeClock());
        var manifestPath = Path.Combine(dir.Path, "trash", entry.Id, "manifest.json");
        var restore = await freshService.RestoreFromManifestAsync(manifestPath);

        Assert.True(restore.IsSuccess);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Restore_refuses_when_something_occupies_the_original_path()
    {
        using var dir = new TempDirectory();
        var file = dir.File("victim.var");
        await File.WriteAllTextAsync(file, "payload");
        var trash = NewService(dir);
        var entry = (await trash.TrashAsync(file, "test")).Value;

        await File.WriteAllTextAsync(file, "something new"); // path re-occupied
        var restore = await trash.RestoreAsync(entry.Id);

        Assert.True(restore.IsFailure);
        Assert.Equal("trash.restore.occupied", restore.Error.Code);
    }

    [Fact]
    public async Task List_enumerates_trashed_items_from_manifests()
    {
        using var dir = new TempDirectory();
        var trash = NewService(dir);
        var a = dir.File("a.var");
        var b = dir.File("b.var");
        await File.WriteAllTextAsync(a, "a");
        await File.WriteAllTextAsync(b, "b");
        await trash.TrashAsync(a, "r1");
        await trash.TrashAsync(b, "r2");

        var list = await trash.ListAsync();
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task Trashing_a_missing_file_fails()
    {
        using var dir = new TempDirectory();
        var trash = NewService(dir);
        Assert.True((await trash.TrashAsync(Path.Combine(dir.Path, "ghost.var"), "x")).IsFailure);
    }
}
