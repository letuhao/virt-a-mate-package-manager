using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class ContentClassificationEngineTests
{
    [Fact]
    public void Classifies_scene_look_clothing_hair_by_path_and_extension()
    {
        var result = ContentClassificationEngine.Classify(
        [
            "meta.json",
            "Saves/scene/MyScene.json",
            "Saves/scene/MyScene.jpg",
            "Custom/Clothing/Female/Dress/dress.vam",
            "Custom/Clothing/Female/Dress/dress.vaj",   // .vaj not in the count rule (matches legacy .vam count)
            "Custom/Hair/Female/Long/long.vam",
            "Custom/Atom/Person/Appearance/look.vap",
        ]);

        Assert.Equal(1, result.Counts[ContentType.Scene]);
        Assert.Equal(1, result.Counts[ContentType.Clothing]);
        Assert.Equal(1, result.Counts[ContentType.Hairstyle]);
        Assert.Equal(1, result.Counts[ContentType.Look]);
        Assert.Equal(ContentType.Scene, result.PrimaryType); // scene wins precedence
    }

    [Fact]
    public void Assets_are_counted_but_not_added_as_preview_items()
    {
        var result = ContentClassificationEngine.Classify(
        [
            "Custom/Assets/env/world.assetbundle",
        ]);

        Assert.Equal(1, result.Counts[ContentType.Asset]);
        Assert.DoesNotContain(result.Items, i => i.Type == ContentType.Asset);
        Assert.Equal(ContentType.Asset, result.PrimaryType);
    }

    [Fact]
    public void Plugin_count_prefers_cslist_over_cs()
    {
        var result = ContentClassificationEngine.Classify(
        [
            "Custom/Scripts/Author/plugin.cs",
            "Custom/Scripts/Author/helper.cs",
            "Custom/Scripts/Author/plugin.cslist",
        ]);

        // cslist present → plugin count = cslist count (1), not the cs count (2).
        Assert.Equal(1, result.Counts[ContentType.Plugin]);
    }

    [Fact]
    public void Plugin_count_falls_back_to_cs_when_no_cslist()
    {
        var result = ContentClassificationEngine.Classify(
        [
            "Custom/Scripts/Author/a.cs",
            "Custom/Scripts/Author/b.cs",
        ]);
        Assert.Equal(2, result.Counts[ContentType.Plugin]);
    }

    [Fact]
    public void Preset_flags_follow_the_legacy_table()
    {
        var result = ContentClassificationEngine.Classify(
        [
            "Custom/Atom/Person/Clothing/preset.vap",  // clothing preset (isPreset when .vap)
            "Custom/Clothing/item/item.vam",           // clothing item (not preset)
        ]);

        Assert.Contains(result.Items, i => i.Type == ContentType.Clothing && i.IsPreset);
        Assert.Contains(result.Items, i => i.Type == ContentType.Clothing && !i.IsPreset);
    }

    [Fact]
    public void Unmatched_entries_are_ignored()
    {
        var result = ContentClassificationEngine.Classify(
        [
            "meta.json",
            "Custom/Atom/Person/Textures/skin.png",   // not in the type table
            "readme.txt",
        ]);

        Assert.Empty(result.Items);
        Assert.Empty(result.Counts);
        Assert.Equal(ContentType.Unknown, result.PrimaryType);
    }

    [Fact]
    public void Classification_is_case_insensitive_and_separator_agnostic()
    {
        var forward = ContentClassificationEngine.Classify(["Custom/Clothing/x/y.vam"]);
        var back = ContentClassificationEngine.Classify([@"CUSTOM\CLOTHING\x\Y.VAM"]);
        Assert.Equal(forward.Counts[ContentType.Clothing], back.Counts[ContentType.Clothing]);
    }
}
