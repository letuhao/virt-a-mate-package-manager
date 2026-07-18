using VarVault.Domain.Indexing;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class VarMetaParserTests
{
    [Fact]
    public void Parses_creator_package_license_and_dependencies()
    {
        const string json = """
        {
          "licenseType": "CC BY-NC-SA",
          "creatorName": "Archer",
          "packageName": "jingjue2333",
          "programVersion": "1.22.0.3",
          "description": "a look",
          "dependencies": {
            "Hunting-Succubus.Enhanced_Eyes.latest": {},
            "Qing.精绝女王.1": {}
          }
        }
        """;

        var meta = VarMetaParser.Parse(json).Value;
        Assert.Equal("Archer", meta.Creator);
        Assert.Equal("jingjue2333", meta.Package);
        Assert.Equal("CC BY-NC-SA", meta.LicenseType);
        Assert.Equal("1.22.0.3", meta.ProgramVersion);
        Assert.Contains("Hunting-Succubus.Enhanced_Eyes.latest", meta.DependencyRefs);
        Assert.Contains("Qing.精绝女王.1", meta.DependencyRefs); // CJK dependency ref preserved
        Assert.Equal(2, meta.DependencyRefs.Count);
    }

    [Fact]
    public void Missing_fields_and_no_dependencies_are_tolerated()
    {
        var meta = VarMetaParser.Parse("""{ "licenseType": "PC" }""").Value;
        Assert.Equal("PC", meta.LicenseType);
        Assert.Null(meta.Creator);
        Assert.Empty(meta.DependencyRefs);
    }

    [Fact]
    public void Malformed_json_is_a_typed_failure_not_an_exception()
    {
        var result = VarMetaParser.Parse("{ not valid json ");
        Assert.True(result.IsFailure);
        Assert.Equal("meta.parse", result.Error.Code);
    }

    [Fact]
    public void Non_object_root_is_a_failure()
    {
        Assert.True(VarMetaParser.Parse("[1,2,3]").IsFailure);
    }
}
