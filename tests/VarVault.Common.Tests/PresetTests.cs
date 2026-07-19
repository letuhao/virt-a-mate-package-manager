using VarVault.Domain.Identity;
using VarVault.Domain.Presets;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class PresetTextFormatTests
{
    [Fact]
    public void Round_trips_refs_ignoring_comments_and_blanks()
    {
        var text = PresetTextFormat.Serialize(["Creator.PkgA.1", "Creator.PkgB.latest"], header: "my preset");
        var parsed = PresetTextFormat.Parse(text);

        Assert.Equal(2, parsed.Count);
        Assert.Contains("Creator.PkgA.1", parsed);
        Assert.Contains("Creator.PkgB.latest", parsed);
    }

    [Fact]
    public void Parse_skips_comments_blanks_and_trims()
    {
        var parsed = PresetTextFormat.Parse("# header\n\n  Creator.Pkg.1  \n#comment\nOther.Pkg.2\n");
        Assert.Equal(["Creator.Pkg.1", "Other.Pkg.2"], parsed);
    }
}

[Trait("Category", TestCategories.Unit)]
public class PresetImporterTests
{
    private static IReadOnlyDictionary<string, IReadOnlyList<AvailablePackageVersion>> Library(
        params (string family, long version, string varName)[] entries)
    {
        var map = new Dictionary<string, IReadOnlyList<AvailablePackageVersion>>(StringComparer.Ordinal);
        foreach (var group in entries.GroupBy(e => IdentityFold.Compute(e.family)))
            map[group.Key] = group.Select(e => new AvailablePackageVersion(e.version, e.varName)).ToList();
        return map;
    }

    [Fact]
    public void Reports_found_substituted_and_unknown()
    {
        var lib = Library(
            ("Creator.PkgA", 1, "Creator.PkgA.1"),
            ("Creator.PkgB", 5, "Creator.PkgB.5"));

        var result = PresetImporter.Analyze(
            ["Creator.PkgA.1", "Creator.PkgB.3", "Ghost.Missing.1", "not-a-ref"], lib);

        Assert.Equal(PresetImportStatus.Found, result.Items[0].Status);           // exact hit
        Assert.Equal("Creator.PkgA.1", result.Items[0].ResolvedVarName);

        Assert.Equal(PresetImportStatus.VersionSubstituted, result.Items[1].Status); // v3 → v5
        Assert.Equal("Creator.PkgB.5", result.Items[1].ResolvedVarName);

        Assert.Equal(PresetImportStatus.Unknown, result.Items[2].Status);          // not in library
        Assert.Equal(PresetImportStatus.Unparseable, result.Items[3].Status);      // bad ref

        Assert.Equal(1, result.FoundCount);
        Assert.Equal(1, result.SubstitutedCount);
        Assert.Equal(2, result.UnknownCount);
    }

    [Fact]
    public void Latest_resolves_to_highest_available_version()
    {
        var lib = Library(("C.P", 1, "C.P.1"), ("C.P", 4, "C.P.4"), ("C.P", 2, "C.P.2"));
        var result = PresetImporter.Analyze(["C.P.latest"], lib);
        Assert.Equal(PresetImportStatus.Found, result.Items[0].Status);
        Assert.Equal("C.P.4", result.Items[0].ResolvedVarName);
    }

    [Fact]
    public void Case_insensitive_family_matching()
    {
        var lib = Library(("Creator.Pkg", 1, "Creator.Pkg.1"));
        var result = PresetImporter.Analyze(["creator.pkg.1"], lib); // different case
        Assert.Equal(PresetImportStatus.Found, result.Items[0].Status);
    }
}
