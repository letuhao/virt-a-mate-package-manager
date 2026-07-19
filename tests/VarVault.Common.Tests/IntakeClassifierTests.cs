using VarVault.Domain.Dedup;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class IntakeClassifierTests
{
    private static VarSignatureFacts Facts(string identity, string? content = null, string? payload = null, string? noPath = null) =>
        new(identity, content, payload, noPath);

    [Fact]
    public void Exact_duplicate_same_identity_same_content()
    {
        var candidate = Facts("a.b.1", content: "sig", payload: "pay");
        var catalog = new[] { Facts("a.b.1", content: "sig", payload: "pay") };
        Assert.Equal(IntakeClass.ExactDuplicate, IntakeClassifier.Classify(candidate, catalog));
    }

    [Fact]
    public void Same_name_different_content_is_a_conflict()
    {
        var candidate = Facts("a.b.1", content: "sigX", payload: "payX");
        var catalog = new[] { Facts("a.b.1", content: "sigY", payload: "payY") };
        Assert.Equal(IntakeClass.SameNameDifferentContent, IntakeClassifier.Classify(candidate, catalog));
    }

    [Fact]
    public void Near_duplicate_different_identity_same_payload()
    {
        // Same content minus meta, different name → near-dup. (4.6)
        var candidate = Facts("alice.x.1", content: "sigA", payload: "pay");
        var catalog = new[] { Facts("bob.x.1", content: "sigB", payload: "pay") };
        Assert.Equal(IntakeClass.NearDuplicate, IntakeClassifier.Classify(candidate, catalog));
    }

    [Fact]
    public void Encoding_variant_matches_by_no_path_signature()
    {
        var candidate = Facts("a.b.1", content: "sigNew", payload: "payNew", noPath: "np");
        var catalog = new[] { Facts("x.y.9", content: "sigOld", payload: "payOld", noPath: "np") };
        Assert.Equal(IntakeClass.EncodingVariant, IntakeClassifier.Classify(candidate, catalog));
    }

    [Fact]
    public void New_when_nothing_matches()
    {
        var candidate = Facts("a.b.1", content: "sig", payload: "pay", noPath: "np");
        var catalog = new[] { Facts("c.d.1", content: "other", payload: "other", noPath: "other") };
        Assert.Equal(IntakeClass.New, IntakeClassifier.Classify(candidate, catalog));
    }

    [Fact]
    public void Exact_wins_over_weaker_matches()
    {
        var candidate = Facts("a.b.1", content: "sig", payload: "pay");
        var catalog = new[]
        {
            Facts("other.x.1", payload: "pay"),   // near-dup
            Facts("a.b.1", content: "sig", payload: "pay"), // exact
        };
        Assert.Equal(IntakeClass.ExactDuplicate, IntakeClassifier.Classify(candidate, catalog));
    }
}
