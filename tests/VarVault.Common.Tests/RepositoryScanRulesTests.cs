using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class RepositoryScanRulesTests
{
    [Theory]
    [InlineData("___VarRedundant___", QuarantineKind.Redundant)]
    [InlineData("___VarRedundant____notSameContentFiles1", QuarantineKind.Redundant)] // legacy suffix
    [InlineData("___StaleVars___", QuarantineKind.Stale)]
    [InlineData("___OldVersionVars___", QuarantineKind.OldVersion)]
    [InlineData("___DeletedVars___", QuarantineKind.Deleted)]
    [InlineData("Vam_Installer_notSameContentFiles1", QuarantineKind.None)]
    [InlineData("Custom", QuarantineKind.None)]
    public void Classifies_quarantine_directories_by_prefix(string dir, QuarantineKind expected)
    {
        Assert.Equal(expected, RepositoryScanRules.ClassifyDirectory(dir));
    }

    [Theory]
    [InlineData("___VarsLink___", true)]
    [InlineData("___MissingVarLink___", true)]
    [InlineData("___TempVarLink___", true)]
    [InlineData("Custom", false)]
    public void Recognizes_link_farm_directories(string dir, bool expected)
    {
        Assert.Equal(expected, RepositoryScanRules.IsLinkDirectory(dir));
    }

    [Fact]
    public void Fresh_when_size_and_mtime_unchanged()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(RepositoryScanRules.IsFresh(100, t, 100, t));
    }

    [Fact]
    public void Not_fresh_when_size_changed()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(RepositoryScanRules.IsFresh(100, t, 101, t));
    }

    [Fact]
    public void Mtime_tolerance_window_applies_for_removable_volumes()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t1 = t.AddSeconds(1.5);
        Assert.True(RepositoryScanRules.IsFresh(100, t, 100, t1, mtimeToleranceSeconds: 2)); // within 2s
        Assert.False(RepositoryScanRules.IsFresh(100, t, 100, t1, mtimeToleranceSeconds: 0)); // exact required
    }
}
