using System.IO;
using VarVault.Domain.Entities;

namespace VarVault.Domain.Import;

/// <summary>A favorite/hide preference recovered from an old-varManager sidecar, keyed by content path.</summary>
/// <param name="ContentPath">Path (relative to the scan root) of the content the sidecar sits beside.</param>
/// <param name="State">The preference the sidecar encodes.</param>
public readonly record struct ImportedPref(string ContentPath, ContentItemPrefState State);

/// <summary>The result of scanning a legacy varManager tree for importable state.</summary>
/// <param name="Prefs">Favorite/hide preferences recovered from <c>.fav</c>/<c>.hide</c> sidecars.</param>
/// <param name="QuarantineDirectories">Recognized quarantine directories (relative paths), for reporting.</param>
public sealed record ImportScanResult(
    IReadOnlyList<ImportedPref> Prefs,
    IReadOnlyList<string> QuarantineDirectories);

/// <summary>
/// Reads state left behind by the old .NET-4.8 varManager: <c>.fav</c>/<c>.hide</c> sidecar files (which
/// sit next to the content they mark) become <see cref="ContentItemPref"/> rows, and its quarantine
/// directories (<c>___VarRedundant____*</c>, <c>___VarnotComplyRule___</c>) are recognized so their
/// contents aren't treated as live library items. Pure parsing + a thin directory walk. (Checklist X.9.)
/// </summary>
public static class VarManagerImport
{
    private const string FavExtension = ".fav";
    private const string HideExtension = ".hide";

    /// <summary>Directory-name prefixes the old varManager used to quarantine content.</summary>
    public static readonly IReadOnlyList<string> QuarantinePrefixes =
    [
        "___VarRedundant",
        "___VarnotComplyRule",
    ];

    /// <summary>
    /// Parse a sidecar filename into the content it marks and the preference it encodes. Returns false
    /// for any other file. E.g. <c>Scene.json.fav</c> → (<c>Scene.json</c>, Favorite).
    /// </summary>
    public static bool TryParseSidecar(string fileName, out string contentName, out ContentItemPrefState state)
    {
        contentName = string.Empty;
        state = ContentItemPrefState.Normal;
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (fileName.EndsWith(FavExtension, StringComparison.OrdinalIgnoreCase))
        {
            state = ContentItemPrefState.Fav;
            contentName = fileName[..^FavExtension.Length];
        }
        else if (fileName.EndsWith(HideExtension, StringComparison.OrdinalIgnoreCase))
        {
            state = ContentItemPrefState.Hide;
            contentName = fileName[..^HideExtension.Length];
        }
        else
        {
            return false;
        }

        // A bare ".fav"/".hide" with no content name in front isn't a valid sidecar.
        return contentName.Length > 0;
    }

    /// <summary>True if <paramref name="directoryName"/> is one of the legacy quarantine folders.</summary>
    public static bool IsQuarantineDirectory(string directoryName) =>
        !string.IsNullOrEmpty(directoryName)
        && QuarantinePrefixes.Any(p => directoryName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Walk <paramref name="root"/>, recovering sidecar preferences and recognizing quarantine
    /// directories. Sidecars found inside quarantine directories are skipped (that content is dead).
    /// </summary>
    public static ImportScanResult Scan(string root)
    {
        Common.Guard.NotNullOrWhiteSpace(root);
        var prefs = new List<ImportedPref>();
        var quarantine = new List<string>();
        if (!Directory.Exists(root))
            return new ImportScanResult(prefs, quarantine);

        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            if (IsQuarantineDirectory(Path.GetFileName(dir)))
                quarantine.Add(Path.GetRelativePath(root, dir));
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!TryParseSidecar(Path.GetFileName(file), out var contentName, out var state))
                continue;

            var relativeDir = Path.GetDirectoryName(Path.GetRelativePath(root, file)) ?? string.Empty;
            // Ignore sidecars under a quarantine directory — that content is not part of the live library.
            if (IsUnderQuarantine(relativeDir))
                continue;

            var contentPath = relativeDir.Length == 0 ? contentName : Path.Combine(relativeDir, contentName);
            prefs.Add(new ImportedPref(contentPath, state));
        }

        return new ImportScanResult(prefs, quarantine);
    }

    private static bool IsUnderQuarantine(string relativeDir) =>
        relativeDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(IsQuarantineDirectory);
}
