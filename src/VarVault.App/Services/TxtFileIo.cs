using System.IO;

namespace VarVault.App.Services;

/// <summary>Shared txt export/import helpers for view-model file-picker hooks.</summary>
public static class TxtFileIo
{
    /// <summary>
    /// Save <paramref name="content"/> via <paramref name="savePicker"/> (suggested filename → local path).
    /// Returns status text; sets <paramref name="lastText"/> only after a path is chosen (or when picker is null for tests).
    /// </summary>
    public static async Task<string> ExportAsync(
        Func<string, Task<string?>>? savePicker,
        string suggestedFileName,
        string content,
        Action<string?>? setLastText = null,
        CancellationToken cancellationToken = default)
    {
        setLastText?.Invoke(content);
        if (savePicker is null)
            return "Export failed: file picker not wired.";
        var path = await savePicker(suggestedFileName).ConfigureAwait(true);
        if (path is not null && path.Length == 0)
            return "Export failed: window not ready for file picker.";
        if (string.IsNullOrWhiteSpace(path))
            return "Export cancelled";
        try
        {
            await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(true);
        }
        catch (IOException ex)
        {
            return $"Export failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Export failed: {ex.Message}";
        }
        return $"Exported to {path}";
    }

    /// <summary>Read non-empty, non-# lines from a utf-8 text file.</summary>
    public static async Task<IReadOnlyList<string>> ReadRefLinesAsync(string path, CancellationToken cancellationToken = default)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        return lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && l[0] != '#')
            .ToList();
    }
}
