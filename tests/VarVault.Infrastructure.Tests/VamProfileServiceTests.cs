using System.IO;
using VarVault.Common;
using VarVault.Domain.Activation;
using VarVault.Infrastructure.Activation;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Loading-profile switching: profiles are directories under <c>___AddonPacksSwitch ___</c> and the
/// switch is a single <c>AddonPackages</c> symlink repoint — flat cost regardless of var count.
/// (Checklist 3.1/3.2.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class VamProfileServiceTests
{
    [Fact]
    public void Switch_root_and_link_paths_use_the_load_bearing_names()
    {
        var svc = new VamProfileService(new FakeSymlinks());
        Assert.EndsWith(Path.Combine("___AddonPacksSwitch ___"), svc.SwitchRoot(@"C:\VaM"));
        Assert.Equal(Path.Combine(@"C:\VaM", "AddonPackages"), svc.AddonPackagesLink(@"C:\VaM"));
    }

    [Fact]
    public void Create_and_list_profiles_round_trips()
    {
        using var root = new TempDirectory();
        var svc = new VamProfileService(new FakeSymlinks());

        Assert.True(svc.CreateProfile(root.Path, "Default").IsSuccess);
        Assert.True(svc.CreateProfile(root.Path, "Studio").IsSuccess);

        var listed = svc.ListProfiles(root.Path);
        Assert.True(listed.IsSuccess);
        Assert.Equal(["Default", "Studio"], listed.Value);
    }

    [Fact]
    public void Create_profile_rejects_path_bearing_names()
    {
        using var root = new TempDirectory();
        var svc = new VamProfileService(new FakeSymlinks());
        Assert.False(svc.CreateProfile(root.Path, @"..\escape").IsSuccess);
    }

    [Fact]
    public void Switch_repoints_the_single_link_and_does_not_walk_the_profile()
    {
        using var root = new TempDirectory();
        var symlinks = new FakeSymlinks();
        var svc = new VamProfileService(symlinks);
        svc.CreateProfile(root.Path, "Big");

        // Fill the profile with many files: switch cost must not scale with them.
        var profileDir = svc.ProfileDirectory(root.Path, "Big");
        for (var i = 0; i < 200; i++)
            File.WriteAllText(Path.Combine(profileDir, $"v{i}.var"), "x");

        var result = svc.SwitchTo(root.Path, "Big");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, symlinks.RepointCalls); // exactly one repoint — O(1), no per-var work
        Assert.Equal((svc.AddonPackagesLink(root.Path), profileDir), symlinks.LastRepoint);
    }

    [Fact]
    public void Switch_to_missing_profile_is_refused()
    {
        using var root = new TempDirectory();
        var symlinks = new FakeSymlinks();
        var svc = new VamProfileService(symlinks);
        var result = svc.SwitchTo(root.Path, "Ghost");
        Assert.False(result.IsSuccess);
        Assert.Equal("profile.missing", result.Error!.Code);
        Assert.Equal(0, symlinks.RepointCalls); // no link touched
    }

    [Fact]
    public void Active_profile_reads_the_link_target_name()
    {
        var symlinks = new FakeSymlinks { LinkTarget = @"C:\VaM\___AddonPacksSwitch ___\Studio" };
        var svc = new VamProfileService(symlinks);
        Assert.Equal("Studio", svc.ActiveProfile(@"C:\VaM"));
    }

    [Fact]
    public void Real_symlink_switch_repoints_addon_packages_when_privilege_allows()
    {
        using var root = new TempDirectory();
        var svc = new VamProfileService(new SymlinkService());
        svc.CreateProfile(root.Path, "P1");
        svc.CreateProfile(root.Path, "P2");
        File.WriteAllText(Path.Combine(svc.ProfileDirectory(root.Path, "P1"), "a.var"), "1");
        File.WriteAllText(Path.Combine(svc.ProfileDirectory(root.Path, "P2"), "b.var"), "2");

        var first = svc.SwitchTo(root.Path, "P1");
        if (!first.IsSuccess && first.Error!.Code == "symlink.privilege")
            return; // sandbox lacks symlink-create privilege — positive path skipped

        Assert.True(first.IsSuccess);
        Assert.Equal("P1", svc.ActiveProfile(root.Path));

        Assert.True(svc.SwitchTo(root.Path, "P2").IsSuccess);
        Assert.Equal("P2", svc.ActiveProfile(root.Path));
        // The active link now surfaces P2's content, and P1's directory is untouched.
        Assert.True(File.Exists(Path.Combine(svc.AddonPackagesLink(root.Path), "b.var")));
        Assert.True(File.Exists(Path.Combine(svc.ProfileDirectory(root.Path, "P1"), "a.var")));
    }

    private sealed class FakeSymlinks : ISymlinkService
    {
        public int RepointCalls { get; private set; }
        public (string Link, string Target) LastRepoint { get; private set; }
        public string? LinkTarget { get; set; }

        public Result RepointDirectory(string linkPath, string newTargetPath)
        {
            RepointCalls++;
            LastRepoint = (linkPath, newTargetPath);
            LinkTarget = newTargetPath;
            return Result.Success();
        }

        public Result CreateDirectory(string linkPath, string targetPath) => Result.Success();
        public Result CreateFile(string linkPath, string targetPath) => Result.Success();
        public Result DeleteLink(string linkPath) => Result.Success();
        public string? ResolveTarget(string linkPath) => LinkTarget;
        public bool IsLink(string path) => LinkTarget is not null;
    }
}
