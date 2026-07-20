using System.Collections.Generic;
using System.IO;
using VarVault.Common;
using VarVault.Domain.Activation;
using VarVault.Infrastructure.Activation;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// doc 26 · G-6 — AddonPackages profile switcher: create / list / switch (active follows) / delete (active
/// protected) / rename (repoints the active symlink). Uses a fake symlink service so it's deterministic.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ProfileSwitcherTests
{
    [Fact]
    public void Create_switch_rename_and_delete_profiles()
    {
        using var vam = new TempDirectory();
        var svc = new VamProfileService(new FakeSymlinks());
        var root = vam.Path;

        Assert.True(svc.CreateProfile(root, "ng9").IsSuccess);
        Assert.True(svc.CreateProfile(root, "UFB").IsSuccess);
        Assert.Equal(2, svc.ListProfiles(root).Value.Count);

        // Switch makes ng9 the active profile.
        Assert.True(svc.SwitchTo(root, "ng9").IsSuccess);
        Assert.Equal("ng9", svc.ActiveProfile(root));

        // The active profile cannot be deleted…
        Assert.False(svc.DeleteProfile(root, "ng9").IsSuccess);
        // …but an inactive one can.
        Assert.True(svc.DeleteProfile(root, "UFB").IsSuccess);
        Assert.Single(svc.ListProfiles(root).Value);

        // Renaming the active profile moves its dir and repoints AddonPackages so it stays active.
        Assert.True(svc.RenameProfile(root, "ng9", "ng9b").IsSuccess);
        Assert.Equal("ng9b", svc.ActiveProfile(root));
        Assert.Contains("ng9b", svc.ListProfiles(root).Value);
        Assert.DoesNotContain("ng9", svc.ListProfiles(root).Value);
    }

    // Simulates the single AddonPackages directory symlink (target tracked in-memory).
    private sealed class FakeSymlinks : ISymlinkService
    {
        private readonly Dictionary<string, string> _links = new();
        public Result CreateDirectory(string linkPath, string targetPath) { _links[linkPath] = targetPath; return Result.Success(); }
        public Result CreateFile(string linkPath, string targetPath) { _links[linkPath] = targetPath; return Result.Success(); }
        public Result RepointDirectory(string linkPath, string newTargetPath) { _links[linkPath] = newTargetPath; return Result.Success(); }
        public Result DeleteLink(string linkPath) { _links.Remove(linkPath); return Result.Success(); }
        public string? ResolveTarget(string linkPath) => _links.TryGetValue(linkPath, out var t) ? t : null;
        public bool IsLink(string path) => _links.ContainsKey(path);
    }
}
