using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace VarVault.App.Services;

/// <summary>
/// Default <see cref="IFileReveal"/>: on Windows opens Explorer with the file selected; otherwise opens the
/// containing folder. Best-effort — a reveal failure must never crash the app. (doc 26 · G-1.2)
/// </summary>
public sealed class FileReveal : IFileReveal
{
    public void Reveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return;
            }

            var dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Best-effort: never let a reveal failure crash the app.
        }
    }
}
