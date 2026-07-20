using System.IO;

namespace VarVault.App.Composition;

/// <summary>
/// Resolves where VarVault keeps its data (catalog.db, thumbnails.db, import temp, logs). This must be decided
/// <b>before</b> the catalog DB is opened, so it can't come from the settings table (which lives inside that DB —
/// chicken-and-egg). Resolution order:
/// <list type="number">
///   <item><c>VARVAULT_DATA_DIR</c> environment variable (portable / power users).</item>
///   <item>A pointer file <c>data-location.txt</c> in the default dir, written by the Settings "change folder" picker.</item>
///   <item>The default: <c>%LOCALAPPDATA%\VarVault</c>.</item>
/// </list>
/// The pointer file always lives in the default dir so bootstrap can always find it; it just names where the data
/// actually lives. Changing the folder takes effect on the next launch (the live DB isn't moved out from under us).
/// </summary>
public static class AppDataLocation
{
    public const string EnvVar = "VARVAULT_DATA_DIR";
    private const string PointerFileName = "data-location.txt";

    /// <summary>The built-in default data directory (<c>%LOCALAPPDATA%\VarVault</c>).</summary>
    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VarVault");

    /// <summary>The pointer file that records a user-chosen data dir (always under <see cref="DefaultDir"/>).</summary>
    public static string PointerFilePath => Path.Combine(DefaultDir, PointerFileName);

    /// <summary>The effective data directory for this run.</summary>
    public static string Resolve()
    {
        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env))
            return env!.Trim();

        try
        {
            if (File.Exists(PointerFilePath))
            {
                var p = File.ReadAllText(PointerFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(p))
                    return p;
            }
        }
        catch { /* unreadable pointer → fall back to default */ }

        return DefaultDir;
    }

    /// <summary>The resolved catalog DB file path (what the user sees in Settings).</summary>
    public static string CatalogDbPath => Path.Combine(Resolve(), "catalog.db");

    /// <summary>The resolved preview-thumbnail cache directory (sharded thumb_*.db files), beside the catalog DB.</summary>
    public static string ThumbnailsDir => Path.Combine(Resolve(), "thumbnails");

    /// <summary>True when the effective data dir is being overridden by the env var (Settings can't change that).</summary>
    public static bool IsOverriddenByEnv => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar));

    /// <summary>
    /// Record a user-chosen data directory (takes effect next launch). Passing the default dir (or null/blank)
    /// clears the override. Never throws — returns false if the pointer couldn't be written.
    /// </summary>
    public static bool SetDataDir(string? dir)
    {
        try
        {
            Directory.CreateDirectory(DefaultDir);
            var normalized = string.IsNullOrWhiteSpace(dir) ? null : Path.GetFullPath(dir!.Trim());
            if (normalized is null || string.Equals(normalized.TrimEnd(Path.DirectorySeparatorChar),
                    DefaultDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(PointerFilePath))
                    File.Delete(PointerFilePath); // back to default
                return true;
            }
            File.WriteAllText(PointerFilePath, normalized);
            return true;
        }
        catch { return false; }
    }
}
