using System.IO;
using VarVault.Sdk.Import;

namespace VarVault.Infrastructure.Import;

/// <summary>
/// A scoped temp directory for one import session; deleted on dispose. The base is chosen so large archives extract
/// on the target repo's own drive (fast same-drive copy, doesn't fill C:) unless a setting overrides it, and a
/// startup sweep removes dirs orphaned by a crash. (doc 30 §6 · D4; doc 31 Phase 3.1.)
/// </summary>
public sealed class TempWorkspace : ITempWorkspace
{
    public string Root { get; }

    public TempWorkspace(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
    }

    public string NewDir(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dir = Path.Combine(Root, string.IsNullOrWhiteSpace(safe) ? "src" : safe);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public ValueTask DisposeAsync()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
        catch { /* best-effort; the startup sweep catches leftovers */ }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The import temp base: the <c>import.temp_dir</c> setting if set, else a hidden dir on the target repo's drive,
    /// else <c>%LOCALAPPDATA%\VarVault\import</c>. Session dirs live directly under this. (§6 · D4.)
    /// </summary>
    public static string ResolveBase(string? settingDir, string? targetRepoMount)
    {
        if (!string.IsNullOrWhiteSpace(settingDir))
            return Path.Combine(settingDir!, "import");
        if (!string.IsNullOrWhiteSpace(targetRepoMount))
        {
            var drive = Path.GetPathRoot(Path.GetFullPath(targetRepoMount!));
            if (!string.IsNullOrEmpty(drive))
                return Path.Combine(drive!, ".varvault-import");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VarVault", "import");
    }

    /// <summary>Create a session-scoped workspace under <see cref="ResolveBase"/>. </summary>
    public static TempWorkspace ForSession(string? settingDir, string? targetRepoMount, Guid sessionId) =>
        new(Path.Combine(ResolveBase(settingDir, targetRepoMount), sessionId.ToString("N")));

    /// <summary>Delete session dirs left behind by a crashed run (called at startup). (§6)</summary>
    public static void SweepOrphans(string baseDir)
    {
        try
        {
            if (!Directory.Exists(baseDir))
                return;
            foreach (var d in Directory.GetDirectories(baseDir))
            {
                try { Directory.Delete(d, recursive: true); }
                catch { /* skip locked ones */ }
            }
        }
        catch { /* best-effort */ }
    }
}
