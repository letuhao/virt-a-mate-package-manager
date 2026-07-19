using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class EncodingFixPolicyTests
{
    [Fact]
    public void Confident_needsfix_auto_fixes()
    {
        var result = new EncodingHealthResult(EncodingHealth.NeedsFix, "GBK", BrokenEntryCount: 0);
        Assert.True(EncodingFixPolicy.IsHighConfidence(result));
        Assert.Equal(FixDisposition.AutoFix, EncodingFixPolicy.Decide(result));
    }

    [Fact]
    public void Partially_broken_is_flagged_for_review()
    {
        var result = new EncodingHealthResult(EncodingHealth.PartiallyBroken, "GBK", BrokenEntryCount: 2);
        Assert.Equal(FixDisposition.FlagForReview, EncodingFixPolicy.Decide(result));
    }

    [Fact]
    public void Undetectable_codepage_is_flagged_not_auto_fixed()
    {
        var result = new EncodingHealthResult(EncodingHealth.NeedsFix, DetectedCodepage: null, BrokenEntryCount: 3);
        Assert.False(EncodingFixPolicy.IsHighConfidence(result));
        Assert.Equal(FixDisposition.FlagForReview, EncodingFixPolicy.Decide(result));
    }

    [Fact]
    public void Healthy_does_nothing()
    {
        Assert.Equal(FixDisposition.None, EncodingFixPolicy.Decide(new EncodingHealthResult(EncodingHealth.Ok, null, 0)));
    }
}

[Trait("Category", TestCategories.Unit)]
public class SlimmingPolicyTests
{
    [Fact]
    public void Default_strips_nothing()
    {
        var options = SlimmingOptions.Off;
        Assert.False(options.StripsAnything);
        foreach (var type in Enum.GetValues<ContentType>())
            Assert.False(SlimmingPolicy.ShouldStrip(type, options));
    }

    [Fact]
    public void Strips_only_the_enabled_types()
    {
        var options = new SlimmingOptions(StripPlugins: true, StripMorphs: true);
        Assert.True(SlimmingPolicy.ShouldStrip(ContentType.Plugin, options));
        Assert.True(SlimmingPolicy.ShouldStrip(ContentType.Morph, options));
        Assert.False(SlimmingPolicy.ShouldStrip(ContentType.Clothing, options));
        Assert.False(SlimmingPolicy.ShouldStrip(ContentType.Scene, options)); // scenes never stripped
    }
}
