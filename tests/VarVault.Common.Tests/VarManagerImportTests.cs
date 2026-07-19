using System.IO;
using VarVault.Domain.Entities;
using VarVault.Domain.Import;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

/// <summary>
/// Importing legacy varManager state: parse <c>.fav</c>/<c>.hide</c> sidecars into preferences and
/// recognize quarantine directories. (Checklist X.9.)
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class VarManagerImportTests
{
    [Theory]
    [InlineData("Scene.json.fav", "Scene.json", ContentItemPrefState.Fav)]
    [InlineData("Look.vap.hide", "Look.vap", ContentItemPrefState.Hide)]
    [InlineData("Creator.Pkg.1.var.FAV", "Creator.Pkg.1.var", ContentItemPrefState.Fav)]
    public void Parses_sidecar_into_content_and_state(string file, string expectName, ContentItemPrefState expectState)
    {
        Assert.True(VarManagerImport.TryParseSidecar(file, out var name, out var state));
        Assert.Equal(expectName, name);
        Assert.Equal(expectState, state);
    }

    [Theory]
    [InlineData("Scene.json")]   // ordinary content, not a sidecar
    [InlineData(".fav")]         // bare sidecar with no content name
    [InlineData("")]
    public void Non_sidecars_are_rejected(string file)
    {
        Assert.False(VarManagerImport.TryParseSidecar(file, out _, out _));
    }

    [Theory]
    [InlineData("___VarRedundant____notSameContentFiles1", true)]
    [InlineData("___VarnotComplyRule___", true)]
    [InlineData("___AddonPacksSwitch ___", false)] // profile switch dir is NOT quarantine
    [InlineData("AddonPackages", false)]
    public void Recognizes_legacy_quarantine_directories(string dir, bool expected)
    {
        Assert.Equal(expected, VarManagerImport.IsQuarantineDirectory(dir));
    }

    [Fact]
    public void Scan_recovers_prefs_and_reports_quarantine_dirs()
    {
        using var root = new TempDirectory();

        // Live content with sidecars.
        File.WriteAllText(Path.Combine(root.Path, "Scene.json"), "{}");
        File.WriteAllText(Path.Combine(root.Path, "Scene.json.fav"), "");
        var sub = Directory.CreateDirectory(Path.Combine(root.Path, "Saves")).FullName;
        File.WriteAllText(Path.Combine(sub, "Look.vap"), "{}");
        File.WriteAllText(Path.Combine(sub, "Look.vap.hide"), "");

        // A quarantine directory whose sidecars must be ignored (dead content).
        var quarantine = Directory.CreateDirectory(Path.Combine(root.Path, "___VarRedundant____removedFiles")).FullName;
        File.WriteAllText(Path.Combine(quarantine, "Dead.json.fav"), "");

        var result = VarManagerImport.Scan(root.Path);

        // Two live prefs, quarantine sidecar excluded.
        Assert.Equal(2, result.Prefs.Count);
        Assert.Contains(result.Prefs, p => p.ContentPath == "Scene.json" && p.State == ContentItemPrefState.Fav);
        Assert.Contains(result.Prefs, p => p.ContentPath == Path.Combine("Saves", "Look.vap") && p.State == ContentItemPrefState.Hide);
        Assert.DoesNotContain(result.Prefs, p => p.ContentPath.Contains("Dead", StringComparison.Ordinal));

        Assert.Contains("___VarRedundant____removedFiles", result.QuarantineDirectories);
    }

    [Fact]
    public void Scan_of_missing_root_is_empty()
    {
        var result = VarManagerImport.Scan(Path.Combine(Path.GetTempPath(), "vv-does-not-exist-" + nameof(VarManagerImportTests)));
        Assert.Empty(result.Prefs);
        Assert.Empty(result.QuarantineDirectories);
    }
}
