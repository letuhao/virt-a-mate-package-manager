using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class DependencyRefTests
{
    [Fact]
    public void Parses_latest_and_exact()
    {
        var latest = DependencyRef.Parse("Hunting-Succubus.Enhanced_Eyes.latest").Value;
        Assert.Equal(VersionSpecKind.Latest, latest.VersionKind);

        var exact = DependencyRef.Parse("Qing.精绝女王.3").Value;
        Assert.Equal(VersionSpecKind.Exact, exact.VersionKind);
        Assert.Equal(3, exact.ExactVersion);
        Assert.Equal("精绝女王", exact.Package); // CJK package name preserved
    }

    [Fact]
    public void Self_ref_is_flagged_when_family_matches_container()
    {
        var containerFamily = Domain.Identity.IdentityFold.Compute("Creator.Package");
        var self = DependencyRef.Parse("Creator.Package.1", containerFamily).Value;
        Assert.True(self.IsSelf);

        var other = DependencyRef.Parse("Other.Pkg.1", containerFamily).Value;
        Assert.False(other.IsSelf);
    }

    [Fact]
    public void Substitution_token_is_flagged_and_treated_as_latest()
    {
        var sub = DependencyRef.Parse("Creator.Package.$VERSION").Value;
        Assert.True(sub.IsSubstitutionToken);
        Assert.Equal(VersionSpecKind.Latest, sub.VersionKind);
    }

    [Theory]
    [InlineData("NotThreeParts")]
    [InlineData("A.B.C.D")]
    [InlineData("A.B.beta")]
    public void Rejects_unparseable_refs(string raw)
    {
        Assert.True(DependencyRef.Parse(raw).IsFailure);
    }

    [Fact]
    public void Family_key_folds_creator_and_package()
    {
        var a = DependencyRef.Parse("Creator.Pkg.1").Value;
        var b = DependencyRef.Parse("creator.pkg.5").Value;
        Assert.Equal(a.FamilyKey, b.FamilyKey); // case-insensitive family grouping
    }
}

[Trait("Category", TestCategories.Unit)]
public class VersionResolverTests
{
    private static readonly AvailableVersion[] Versions =
    [
        new(1, 101), new(3, 103), new(5, 105),
    ];

    [Fact]
    public void Latest_picks_highest_version()
    {
        var r = VersionResolver.Resolve(Ref("latest"), Versions)!;
        Assert.Equal(105, r.PackageId);
        Assert.Equal(ResolvedVia.Latest, r.Via);
        Assert.False(r.IsVersionSubstituted);
    }

    [Fact]
    public void Exact_hit_resolves_without_substitution()
    {
        var r = VersionResolver.Resolve(Ref("3"), Versions)!;
        Assert.Equal(103, r.PackageId);
        Assert.Equal(ResolvedVia.Exact, r.Via);
        Assert.False(r.IsVersionSubstituted);
    }

    [Fact]
    public void Exact_miss_prefers_closest_newer()
    {
        var r = VersionResolver.Resolve(Ref("2"), Versions)!; // no v2 → closest newer is v3
        Assert.Equal(103, r.PackageId);
        Assert.Equal(ResolvedVia.Closest, r.Via);
        Assert.True(r.IsVersionSubstituted);
    }

    [Fact]
    public void Exact_miss_falls_back_to_newest_older_when_no_newer()
    {
        var r = VersionResolver.Resolve(Ref("9"), Versions)!; // nothing newer than 9 → newest older is v5
        Assert.Equal(105, r.PackageId);
        Assert.Equal(ResolvedVia.Closest, r.Via);
        Assert.True(r.IsVersionSubstituted);
    }

    [Fact]
    public void No_available_versions_is_unresolved()
    {
        Assert.Null(VersionResolver.Resolve(Ref("1"), []));
    }

    private static DependencyRef Ref(string version) => DependencyRef.Parse($"Creator.Package.{version}").Value;
}
