using VarVault.Domain.Entities;

namespace VarVault.Domain.Content;

/// <summary>
/// Optional slimming options — which content types to strip when producing a slimmed var. A separate,
/// <b>off-by-default</b> step layered on the encoding fix (from Boss963), never part of the pure fix.
/// (Checklist 4.17.)
/// </summary>
public sealed record SlimmingOptions(
    bool StripPlugins = false,
    bool StripClothing = false,
    bool StripHair = false,
    bool StripAssets = false,
    bool StripPose = false,
    bool StripMorphs = false)
{
    /// <summary>The default: strip nothing (slimming is opt-in).</summary>
    public static readonly SlimmingOptions Off = new();

    public bool StripsAnything =>
        StripPlugins || StripClothing || StripHair || StripAssets || StripPose || StripMorphs;
}

/// <summary>Decides whether a content type is stripped under a set of slimming options. (4.17.)</summary>
public static class SlimmingPolicy
{
    public static bool ShouldStrip(ContentType type, SlimmingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return type switch
        {
            ContentType.Plugin => options.StripPlugins,
            ContentType.Clothing => options.StripClothing,
            ContentType.Hairstyle => options.StripHair,
            ContentType.Asset => options.StripAssets,
            ContentType.Pose => options.StripPose,
            ContentType.Morph => options.StripMorphs,
            _ => false,
        };
    }
}
