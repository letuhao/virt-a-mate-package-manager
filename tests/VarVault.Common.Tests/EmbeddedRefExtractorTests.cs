using VarVault.Domain.Dependencies;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class EmbeddedRefExtractorTests
{
    [Fact]
    public void Extracts_packaged_refs_from_scene_json()
    {
        const string json = """
        {
          "storables": [
            { "id": "geometry", "clothing": "Creator.Dress.2:/Custom/Clothing/Female/d.vam" },
            { "hair": "Other.Hair.latest:/Custom/Hair/h.vam" },
            { "local": "Custom/Clothing/local.vam" }
          ]
        }
        """;

        var refs = EmbeddedRefExtractor.Extract(json);

        Assert.Contains("Creator.Dress.2", refs);
        Assert.Contains("Other.Hair.latest", refs);
        Assert.DoesNotContain("Custom/Clothing/local.vam", refs); // local (unpackaged) path ignored
    }

    [Fact]
    public void Deduplicates_repeated_refs()
    {
        const string json = """{ "a": "C.P.1:/x", "b": "C.P.1:/y" }""";
        Assert.Single(EmbeddedRefExtractor.Extract(json));
    }

    [Fact]
    public void Extracts_cjk_creator_refs()
    {
        const string json = """{ "look": "刘亦菲.衣装.1:/Custom/Clothing/x.vam" }""";
        Assert.Contains("刘亦菲.衣装.1", EmbeddedRefExtractor.Extract(json));
    }

    [Fact]
    public void Empty_or_refless_json_yields_nothing()
    {
        Assert.Empty(EmbeddedRefExtractor.Extract("{}"));
        Assert.Empty(EmbeddedRefExtractor.Extract("""{ "note": "no refs here" }"""));
    }
}
