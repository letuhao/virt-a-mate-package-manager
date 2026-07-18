using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.ValueObjects;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class IdentityFacetsTests
{
    [Fact]
    public void Parses_three_part_name_with_verbatim_version()
    {
        var id = PackageId.TryParse("Creator.Package.007").Value;
        Assert.Equal("Creator", id.Creator);
        Assert.Equal("Package", id.Package);
        Assert.Equal("007", id.VersionToken);
        Assert.Equal(7, id.VersionSort);
    }

    [Fact]
    public void Rejects_non_three_part_or_non_numeric_version()
    {
        Assert.True(PackageId.TryParse("A.B.C.1").IsFailure);   // 4 parts
        Assert.True(PackageId.TryParse("A.B.beta").IsFailure);  // non-numeric version
        Assert.True(PackageId.TryParse("JustAName").IsFailure); // 1 part
    }

    [Fact]
    public void Dot_seven_and_dot_zerozeroseven_stay_distinct_versions()
    {
        var a = PackageId.TryParse("C.P.7").Value;
        var b = PackageId.TryParse("C.P.007").Value;
        Assert.NotEqual(a.VarName, b.VarName);        // display distinct
        Assert.NotEqual(a.IdentityKey, b.IdentityKey); // identity distinct
        Assert.Equal(a.VersionSort, b.VersionSort);    // numeric sort equal (both 7)
    }

    [Fact]
    public void Long_version_clamps_without_overflow()
    {
        // 11+ digits would overflow int; clamp to long.MaxValue rather than throw/overflow.
        var id = PackageId.TryParse("C.P.99999999999999999999").Value;
        Assert.Equal(long.MaxValue, id.VersionSort);
        Assert.Equal("99999999999999999999", id.VersionToken); // verbatim preserved
    }

    [Fact]
    public void Version_sort_parses_normal_values()
    {
        Assert.Equal(0, PackageId.ParseVersionSort("0"));
        Assert.Equal(42, PackageId.ParseVersionSort("42"));
        Assert.Equal(7, PackageId.ParseVersionSort("007"));
    }
}

[Trait("Category", TestCategories.Unit)]
public class GenderInferenceTests
{
    [Fact]
    public void Infers_female_from_paths()
    {
        var g = GenderInference.Infer(["Custom/Clothing/Female/Dress/dress.vam"]);
        Assert.Equal(Gender.Female, g.Gender);
        Assert.True(g.Confidence > 0);
    }

    [Fact]
    public void Infers_male_without_matching_inside_female()
    {
        var g = GenderInference.Infer(["Custom/Clothing/Male/Suit/suit.vam"]);
        Assert.Equal(Gender.Male, g.Gender);
    }

    [Fact]
    public void Female_path_does_not_infer_male()
    {
        var g = GenderInference.Infer(["Custom/Clothing/Female/x.vam"]);
        Assert.Equal(Gender.Female, g.Gender); // "male" inside "female" must not win
    }

    [Fact]
    public void Infers_futa_even_when_female_also_present()
    {
        var g = GenderInference.Infer(["Custom/Clothing/Futa/Female/x.vam"]);
        Assert.Equal(Gender.Futa, g.Gender);
    }

    [Fact]
    public void No_gender_fragment_yields_auto_zero_confidence()
    {
        var g = GenderInference.Infer(["Custom/Clothing/Unisex/x.vam"]);
        Assert.Equal(Gender.Auto, g.Gender);
        Assert.Equal(0, g.Confidence);
    }

    [Fact]
    public void Confidence_reflects_vote_share()
    {
        var g = GenderInference.Infer(
        [
            "Custom/Clothing/Female/a.vam",
            "Custom/Clothing/Female/b.vam",
            "Custom/Clothing/Male/c.vam",
        ]);
        Assert.Equal(Gender.Female, g.Gender);
        Assert.Equal(2.0 / 3.0, g.Confidence, precision: 6);
    }
}
