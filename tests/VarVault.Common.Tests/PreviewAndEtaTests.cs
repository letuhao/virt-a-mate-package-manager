using VarVault.Domain.Analyzer;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class PreviewRulesTests
{
    [Theory]
    [InlineData(ContentType.Scene, true)]
    [InlineData(ContentType.Look, true)]
    [InlineData(ContentType.Asset, false)]
    [InlineData(ContentType.Morph, false)]
    [InlineData(ContentType.Plugin, false)]
    public void Preview_less_types_get_a_placeholder(ContentType type, bool hasPreview)
    {
        Assert.Equal(hasPreview, PreviewRules.HasPreview(type));
    }

    [Fact]
    public void Representative_type_is_the_primary_type()
    {
        var counts = new Dictionary<ContentType, int> { [ContentType.Look] = 2, [ContentType.Scene] = 1 };
        Assert.Equal(ContentType.Scene, PreviewRules.RepresentativeType(counts)); // scene precedence
    }

    [Fact]
    public void Sibling_jpg_path_swaps_the_extension()
    {
        Assert.Equal("Saves/scene/x.jpg", PreviewRules.SiblingJpgPath("Saves/scene/x.json"));
        Assert.Equal("Custom/Clothing/d.jpg", PreviewRules.SiblingJpgPath("Custom/Clothing/d.vam"));
    }
}

[Trait("Category", TestCategories.Unit)]
public class MigrationEtaTests
{
    [Fact]
    public void Eta_is_bytes_over_write_speed()
    {
        // 100 MB at 100 MB/s → ~1 s.
        var eta = MigrationEta.Estimate(100L * 1024 * 1024, targetWriteMBps: 100);
        Assert.Equal(1.0, eta.TotalSeconds, precision: 2);
    }

    [Fact]
    public void Zero_bytes_or_speed_is_zero()
    {
        Assert.Equal(TimeSpan.Zero, MigrationEta.Estimate(0, 100));
        Assert.Equal(TimeSpan.Zero, MigrationEta.Estimate(1000, 0));
    }
}
