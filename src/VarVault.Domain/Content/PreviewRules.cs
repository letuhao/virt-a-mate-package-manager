using System.IO;
using VarVault.Domain.Entities;

namespace VarVault.Domain.Content;

/// <summary>
/// Preview rules: which content types carry a visible preview, the representative preview type for a
/// package (its PrimaryType), and the sibling <c>.jpg</c> path for a content entry. (Checklist 1.32/1.34.)
/// </summary>
public static class PreviewRules
{
    /// <summary>Types that have no visible preview and get a type placeholder instead. (1.34)</summary>
    public static bool HasPreview(ContentType type) => type switch
    {
        ContentType.Asset => false,
        ContentType.Morph => false,
        ContentType.Plugin => false,
        ContentType.Unknown => false,
        _ => true,
    };

    /// <summary>The representative preview type for a package = its primary type (precedence). </summary>
    public static ContentType RepresentativeType(IReadOnlyDictionary<ContentType, int> counts) =>
        ContentClassificationEngine.ChoosePrimary(counts);

    /// <summary>The sibling preview path for a content entry (same path, <c>.jpg</c> extension). (1.32)</summary>
    public static string SiblingJpgPath(string entryPath)
    {
        ArgumentNullException.ThrowIfNull(entryPath);
        var withoutExt = entryPath[..^Path.GetExtension(entryPath).Length];
        return withoutExt + ".jpg";
    }
}
