using System.IO;
using VarVault.Infrastructure.Import;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>Slice B (doc 31 Phase 3.1): the scoped temp workspace creates + cleans, and the startup sweep removes
/// dirs a crash left behind; the base resolves to a setting, then the target drive, then LocalAppData. (§6 · D4.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class TempWorkspaceTests
{
    [Fact]
    public async Task Creates_then_deletes_on_dispose()
    {
        using var dir = new TempDirectory();
        var root = Path.Combine(dir.Path, "session1");
        string sub;
        await using (var ws = new TempWorkspace(root))
        {
            sub = ws.NewDir("pack.zip_abc");
            File.WriteAllText(Path.Combine(sub, "x.var"), "y");
            Assert.True(Directory.Exists(sub));
        }
        Assert.False(Directory.Exists(root)); // gone after dispose
    }

    [Fact]
    public void Sweep_removes_orphaned_session_dirs()
    {
        using var dir = new TempDirectory();
        var baseDir = Path.Combine(dir.Path, "import");
        var orphan = Directory.CreateDirectory(Path.Combine(baseDir, "crashed_session")).FullName;
        File.WriteAllText(Path.Combine(orphan, "leftover.var"), "z");

        TempWorkspace.SweepOrphans(baseDir);

        Assert.False(Directory.Exists(orphan));
    }

    [Fact]
    public void ResolveBase_prefers_setting_then_target_drive_then_localappdata()
    {
        Assert.StartsWith(@"X:\custom", TempWorkspace.ResolveBase(@"X:\custom", @"D:\repo"));
        var onTargetDrive = TempWorkspace.ResolveBase(null, @"D:\VarVault_repo");
        Assert.StartsWith(@"D:\", onTargetDrive);
        var fallback = TempWorkspace.ResolveBase(null, null);
        Assert.Contains("VarVault", fallback);
    }
}
