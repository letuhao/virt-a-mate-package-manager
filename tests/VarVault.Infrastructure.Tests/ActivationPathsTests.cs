using System.IO;
using VarVault.Domain.Activation;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>Path/identity rules for on-disk activation links (spec 21 §3.4; checklist 22 · T2.1).</summary>
[Trait("Category", TestCategories.Unit)]
public sealed class ActivationPathsTests
{
    [Fact]
    public void LinkFileName_is_verbatim_varname_with_leading_zero_version_preserved()
    {
        var result = ActivationPaths.LinkFileName("Creator.Package.007");
        Assert.True(result.IsSuccess);
        Assert.Equal("Creator.Package.007.var", result.Value); // .007 stays .007 — VaM keys on this
    }

    [Fact]
    public void LinkFileName_accepts_a_dotvar_suffixed_name()
    {
        var result = ActivationPaths.LinkFileName("Creator.Package.3.var");
        Assert.True(result.IsSuccess);
        Assert.Equal("Creator.Package.3.var", result.Value);
    }

    [Theory]
    [InlineData("NotAThreePartName")]
    [InlineData("Creator.Package")]
    [InlineData("Creator.Package.latest")]   // 'latest' is not a materializable version
    [InlineData("Creator.Package.v3")]
    public void LinkFileName_rejects_malformed_names(string bad)
    {
        Assert.True(ActivationPaths.LinkFileName(bad).IsFailure);
    }

    [Fact]
    public void AliasLinkFileName_keeps_numeric_missing_ref()
    {
        var result = ActivationPaths.AliasLinkFileName("Gone.Missing.1", "Owned.Sub.9");
        Assert.True(result.IsSuccess);
        Assert.Equal("Gone.Missing.1.var", result.Value);
    }

    [Fact]
    public void AliasLinkFileName_rewrites_latest_to_target_version()
    {
        // Legacy Createlink: Creator.Pkg.latest + dest Creator.Pkg.7 → Creator.Pkg.7.var
        var result = ActivationPaths.AliasLinkFileName("Creator.Pkg.latest", "Creator.Pkg.7");
        Assert.True(result.IsSuccess);
        Assert.Equal("Creator.Pkg.7.var", result.Value);
    }

    [Fact]
    public void SourcePath_combines_mount_and_relative()
    {
        var path = ActivationPaths.SourcePath(@"F:\Repo", @"___VarTidied___\Creator\Creator.Package.1.var");
        Assert.Equal(Path.Combine(@"F:\Repo", @"___VarTidied___\Creator\Creator.Package.1.var"), path);
    }

    [Fact]
    public void Profile_dirs_use_the_load_bearing_names_verbatim()
    {
        var vars = ActivationPaths.VarsLinkDir(@"F:\VaM", "ng9");
        var missing = ActivationPaths.MissingVarLinkDir(@"F:\VaM", "ng9");

        // The space before the trailing underscores in the switch dir is literal.
        Assert.Contains("___AddonPacksSwitch ___", vars);
        Assert.EndsWith(Path.Combine("ng9", "___VarsLink___"), vars);
        Assert.EndsWith(Path.Combine("ng9", "___MissingVarLink___"), missing);
    }
}
