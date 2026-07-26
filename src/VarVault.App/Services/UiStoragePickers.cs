using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace VarVault.App.Services;

/// <summary>Avalonia StorageProvider wrappers for txt open/save from UserControls.</summary>
public static class UiStoragePickers
{
    public static async Task<string?> SaveTxtAsync(Control host, string suggestedFileName, string title = "Save text file")
    {
        var top = await ResolveTopLevelAsync(host).ConfigureAwait(true);
        if (top is null)
            return string.Empty; // distinct from cancel (null): window/picker host unavailable
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "txt",
            FileTypeChoices =
            [
                new FilePickerFileType("Text") { Patterns = ["*.txt"] },
                FilePickerFileTypes.All,
            ],
        }).ConfigureAwait(true);
        return file is null ? null : file.TryGetLocalPath() ?? file.Path.LocalPath;
    }

    public static async Task<string?> OpenTxtAsync(Control host, string title = "Open text file")
    {
        var top = await ResolveTopLevelAsync(host).ConfigureAwait(true);
        if (top is null)
            return string.Empty; // distinct from cancel (null)
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Text") { Patterns = ["*.txt"] },
                FilePickerFileTypes.All,
            ],
        }).ConfigureAwait(true);
        if (files.Count == 0)
            return null;
        var file = files[0];
        return file.TryGetLocalPath() ?? file.Path.LocalPath;
    }

    private static async Task<TopLevel?> ResolveTopLevelAsync(Control host)
    {
        var top = TopLevel.GetTopLevel(host) ?? host.GetVisualRoot() as TopLevel;
        if (top is not null)
            return top;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Loaded);
        return TopLevel.GetTopLevel(host) ?? host.GetVisualRoot() as TopLevel;
    }
}
