using System.IO;
using VarVault.Infrastructure.Activation;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// The activation symlink mechanism: create a directory symlink and repoint it (the instant preset
/// switch), never touching the target's contents. Skips where symlink creation is unavailable
/// (no Developer Mode / privilege), asserting the error is clear. (Checklist 3.2/3.3.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class SymlinkServiceTests
{
    private readonly SymlinkService _sut = new();

    [Fact]
    public void Creates_and_repoints_a_directory_symlink_without_deleting_targets()
    {
        using var dir = new TempDirectory();
        var profileA = Path.Combine(dir.Path, "ProfileA");
        var profileB = Path.Combine(dir.Path, "ProfileB");
        Directory.CreateDirectory(profileA);
        Directory.CreateDirectory(profileB);
        File.WriteAllText(Path.Combine(profileA, "a.var"), "A");
        File.WriteAllText(Path.Combine(profileB, "b.var"), "B");

        var link = Path.Combine(dir.Path, "AddonPackages");

        var create = _sut.CreateDirectory(link, profileA);
        if (create.IsFailure)
        {
            Assert.Equal("symlink.privilege", create.Error.Code); // 3.3 clear error when unavailable
            return;
        }

        // The link surfaces ProfileA's contents.
        Assert.True(_sut.IsLink(link));
        Assert.True(File.Exists(Path.Combine(link, "a.var")));

        // Repoint = instant switch to ProfileB (3.2).
        var repoint = _sut.RepointDirectory(link, profileB);
        Assert.True(repoint.IsSuccess, repoint.Error.ToString());
        Assert.True(File.Exists(Path.Combine(link, "b.var")));
        Assert.False(File.Exists(Path.Combine(link, "a.var")));

        // Both targets and their files survive the repoint.
        Assert.True(File.Exists(Path.Combine(profileA, "a.var")));
        Assert.True(File.Exists(Path.Combine(profileB, "b.var")));
    }

    [Fact]
    public void Resolve_target_returns_the_link_destination()
    {
        using var dir = new TempDirectory();
        var target = Path.Combine(dir.Path, "target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(dir.Path, "link");

        if (_sut.CreateDirectory(link, target).IsFailure)
            return; // no symlink privilege

        Assert.Equal(target, _sut.ResolveTarget(link));
    }

    [Fact]
    public void DeleteLink_removes_file_symlink_leaves_target()
    {
        using var dir = new TempDirectory();
        var target = Path.Combine(dir.Path, "source.var");
        File.WriteAllText(target, "payload");
        var link = Path.Combine(dir.Path, "link.var");

        if (_sut.CreateFile(link, target).IsFailure)
            return; // no symlink privilege

        Assert.True(_sut.IsLink(link));
        var result = _sut.DeleteLink(link);

        Assert.True(result.IsSuccess, result.Error.ToString());
        Assert.False(File.Exists(link));            // link gone
        Assert.True(File.Exists(target));           // target survives
        Assert.Equal("payload", File.ReadAllText(target));
    }

    [Fact]
    public void DeleteLink_removes_directory_symlink_leaves_target()
    {
        using var dir = new TempDirectory();
        var target = Path.Combine(dir.Path, "targetdir");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.var"), "x");
        var link = Path.Combine(dir.Path, "dlink");

        if (_sut.CreateDirectory(link, target).IsFailure)
            return; // no symlink privilege

        Assert.True(_sut.DeleteLink(link).IsSuccess);
        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "keep.var")));
    }

    [Fact]
    public void DeleteLink_missing_path_is_success()
    {
        using var dir = new TempDirectory();
        var result = _sut.DeleteLink(Path.Combine(dir.Path, "does-not-exist.var"));
        Assert.True(result.IsSuccess); // idempotent
    }

    [Fact]
    public void DeleteLink_refuses_a_real_file()
    {
        using var dir = new TempDirectory();
        var real = Path.Combine(dir.Path, "real.var");
        File.WriteAllText(real, "important");

        var result = _sut.DeleteLink(real);

        Assert.True(result.IsFailure);
        Assert.Equal("symlink.notalink", result.Error.Code);
        Assert.True(File.Exists(real)); // never clobber a real file
    }

    [Fact]
    public void Repoint_refuses_to_replace_a_real_directory()
    {
        using var dir = new TempDirectory();
        var real = Path.Combine(dir.Path, "real");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "keep.var"), "x");

        var result = _sut.RepointDirectory(real, Path.Combine(dir.Path, "other"));
        Assert.True(result.IsFailure);
        Assert.Equal("symlink.notalink", result.Error.Code); // never clobber a real directory
        Assert.True(File.Exists(Path.Combine(real, "keep.var")));
    }
}
