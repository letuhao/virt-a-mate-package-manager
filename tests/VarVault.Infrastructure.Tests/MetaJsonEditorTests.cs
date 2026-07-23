using VarVault.Domain.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Unit)]
public sealed class MetaJsonEditorTests
{
    [Fact]
    public void Apply_preserves_unknown_keys_and_rewrites_dependencies()
    {
        const string raw = """
            {
              "creatorName": "Old",
              "packageName": "Pack",
              "licenseType": "CC BY",
              "promotionalLink": "https://example.test",
              "dependencies": {
                "A.B.1": { "licenseType": "FC", "dependencies": {} },
                "C.D.latest": { "licenseType": "", "dependencies": {} }
              }
            }
            """;

        var result = MetaJsonEditor.Apply(
            raw,
            creatorName: "New",
            packageName: "Pack",
            licenseType: "CC0",
            description: "hi",
            programVersion: "1.20",
            dependencyRefs: ["X.Y.2", "Z.W.latest"]);

        Assert.True(result.IsSuccess, result.Error.ToString());
        Assert.Contains("\"promotionalLink\"", result.Value);
        Assert.Contains("https://example.test", result.Value);
        Assert.Contains("\"creatorName\":\"New\"", result.Value.Replace(" ", ""));
        Assert.Contains("X.Y.2", result.Value);
        Assert.Contains("Z.W.latest", result.Value);
        Assert.DoesNotContain("A.B.1", result.Value);
    }

    [Fact]
    public void Apply_preserves_nested_dependency_payload_for_kept_refs()
    {
        const string raw = """
            {
              "creatorName": "Old",
              "packageName": "Pack",
              "dependencies": {
                "A.B.1": {
                  "licenseType": "FC",
                  "dependencies": { "N.Nested.2": { "licenseType": "", "dependencies": {} } }
                }
              }
            }
            """;

        var result = MetaJsonEditor.Apply(
            raw,
            creatorName: "Old",
            packageName: "Pack",
            licenseType: null,
            description: "only-desc",
            programVersion: null,
            dependencyRefs: ["A.B.1", "X.Y.2"]);

        Assert.True(result.IsSuccess, result.Error.ToString());
        var compact = result.Value.Replace(" ", "").Replace("\r", "").Replace("\n", "");
        Assert.Contains("\"licenseType\":\"FC\"", compact);
        Assert.Contains("N.Nested.2", result.Value);
        Assert.Contains("X.Y.2", result.Value);
        Assert.Contains("only-desc", result.Value);
    }

    [Fact]
    public void Apply_rejects_invalid_dependency_ref()
    {
        var result = MetaJsonEditor.Apply(
            "{}",
            null, null, null, null, null,
            ["not-a-ref"]);
        Assert.True(result.IsFailure);
        Assert.Equal("dep.format", result.Error.Code);
    }
}
