using VarVault.Domain.Identity;
using VarVault.Domain.ValueObjects;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class IdentityFoldTests
{
    [Fact]
    public void Folds_ascii_case_insensitively()
    {
        // 0.11 — ASCII case-fold
        Assert.Equal(IdentityFold.Compute("Café"), IdentityFold.Compute("café"));
        Assert.Equal(IdentityFold.Compute("Creator.Pkg.007"), IdentityFold.Compute("creator.pkg.007"));
    }

    [Fact]
    public void Folds_cyrillic_case_insensitively()
    {
        // 0.11 — non-ASCII (Cyrillic) case-fold: "МЕ" == "ме"
        Assert.Equal(IdentityFold.Compute("МЕ"), IdentityFold.Compute("ме"));
    }

    [Fact]
    public void Preserves_cjk_which_is_caseless()
    {
        // CJK has no case; the key must round-trip the characters (only normalized).
        var key = IdentityFold.Compute("刘亦菲.衣装.1");
        Assert.Contains("刘亦菲", key);
    }

    [Fact]
    public void Normalizes_composed_and_decomposed_forms_equal()
    {
        // "é" precomposed (U+00E9) vs "e" + combining acute (U+0065 U+0301) fold equal via NFC.
        var precomposed = "Café";
        var decomposed = "Café";
        Assert.Equal(IdentityFold.Compute(precomposed), IdentityFold.Compute(decomposed));
    }

    [Fact]
    public void Is_idempotent()
    {
        var once = IdentityFold.Compute("Creator.Pkg.007");
        Assert.Equal(once, IdentityFold.Compute(once));
    }

    [Fact]
    public void Empty_input_returns_empty()
    {
        Assert.Equal(string.Empty, IdentityFold.Compute(string.Empty));
    }

    [Fact]
    public void Distinct_identities_do_not_collide()
    {
        Assert.NotEqual(IdentityFold.Compute("Creator.PkgA.1"), IdentityFold.Compute("Creator.PkgB.1"));
    }

    [Fact]
    public void PackageId_identity_key_matches_fold_of_varname()
    {
        var id = PackageId.TryParse("Creator.Pkg.007").Value!;
        Assert.Equal(IdentityFold.Compute("Creator.Pkg.007"), id.IdentityKey);
        // Verbatim version token preserved for display.
        Assert.Equal("007", id.VersionToken);
    }
}
