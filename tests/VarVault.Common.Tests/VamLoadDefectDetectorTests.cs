using System.Text;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.Fingerprinting;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public sealed class VamLoadDefectDetectorTests
{
    [Fact]
    public void Clean_entries_produce_no_structural_defects()
    {
        var entries = new[]
        {
            Entry("meta.json"),
            Entry("Custom/Clothing/a.vam"),
            Entry("Custom/Clothing/b.vam"),
        };

        var defects = VamLoadDefectDetector.Detect(entries);
        Assert.Empty(defects);
    }

    [Fact]
    public void Slash_vs_backslash_collides_as_DuplicateEntries()
    {
        var entries = new[]
        {
            Entry("meta.json"),
            Entry("Custom/Clothing/Same.vam"),
            Entry(@"Custom\Clothing\Same.vam"),
        };

        var defects = VamLoadDefectDetector.Detect(entries);
        Assert.Contains(defects, d => d.Kind == VamLoadDefectKind.DuplicateEntries);
        Assert.Equal(IntegrityStatus.DuplicateEntries, VamLoadDefectDetector.SuggestedIntegrityStatus(defects));
    }

    [Fact]
    public void Case_twins_collide_as_DuplicateEntries()
    {
        var entries = new[]
        {
            Entry("meta.json"),
            Entry("Custom/Foo.vam"),
            Entry("custom/foo.vam"),
        };

        var defects = VamLoadDefectDetector.Detect(entries);
        Assert.Contains(defects, d => d.Kind == VamLoadDefectKind.DuplicateEntries);
    }

    [Fact]
    public void Exact_raw_duplicate_names_collide()
    {
        var entries = new[]
        {
            Entry("meta.json"),
            Entry("Custom/dup.vam"),
            Entry("Custom/dup.vam"),
        };

        var defects = VamLoadDefectDetector.Detect(entries);
        Assert.Contains(defects, d => d.Kind == VamLoadDefectKind.DuplicateEntries
                                      && d.Detail.Contains("Custom/dup.vam", StringComparison.Ordinal));
    }

    [Fact]
    public void Zip_open_failure_maps_to_CorruptZip_status()
    {
        var defects = VamLoadDefectDetector.Detect(
            [Entry("meta.json")],
            zipOpenFailed: true,
            zipOpenError: "ArgumentException: same key");
        Assert.Contains(defects, d => d.Kind == VamLoadDefectKind.ZipOpenFailed);
        Assert.Equal(IntegrityStatus.CorruptZip, VamLoadDefectDetector.SuggestedIntegrityStatus(defects));
    }

    [Fact]
    public void DuplicateEntries_wins_over_ZipOpenFailed_for_catalog_status()
    {
        var entries = new[]
        {
            Entry("meta.json"),
            Entry("Custom/Mons Pubis.vmb"),
            Entry("Custom/Mons pubis.vmb"),
        };
        var defects = VamLoadDefectDetector.Detect(entries, zipOpenFailed: true, zipOpenError: "ArgumentException");
        Assert.Contains(defects, d => d.Kind == VamLoadDefectKind.DuplicateEntries);
        Assert.Contains(defects, d => d.Kind == VamLoadDefectKind.ZipOpenFailed);
        Assert.Equal(IntegrityStatus.DuplicateEntries, VamLoadDefectDetector.SuggestedIntegrityStatus(defects));
    }

    [Fact]
    public void ListCollisions_suggests_keep_larger()
    {
        var entries = new[]
        {
            new ZipEntryFacts(Encoding.UTF8.GetBytes("meta.json"), 10, 1, false, NameIsUtf8: true),
            new ZipEntryFacts(Encoding.UTF8.GetBytes("Custom/Mons Pubis.vmb"), 100, 2, false, NameIsUtf8: true),
            new ZipEntryFacts(Encoding.UTF8.GetBytes("Custom/Mons pubis.vmb"), 500, 3, false, NameIsUtf8: true),
        };

        var groups = VamLoadDefectDetector.ListCollisions(entries);
        Assert.Single(groups);
        Assert.Equal(2, groups[0].Members.Count);
        Assert.Equal("Custom/Mons pubis.vmb", groups[0].SuggestedKeepFullName);
    }

    [Fact]
    public void NormalizeEntryKey_collapses_separators_and_dot_slash()
    {
        Assert.Equal("Custom/a.vam", VamLoadDefectDetector.NormalizeEntryKey(@"Custom\a.vam"));
        Assert.Equal("Custom/a.vam", VamLoadDefectDetector.NormalizeEntryKey("./Custom/a.vam"));
    }

    private static ZipEntryFacts Entry(string name) =>
        new(Encoding.UTF8.GetBytes(name), 1, 0, name.EndsWith('/') || name.EndsWith('\\'), NameIsUtf8: true);
}
