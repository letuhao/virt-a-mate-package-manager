using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

/// <summary>SCR-13 · Settings screen. Wires the real folder picker for the VaM install path. (Checklist 22 · T6.3a.)</summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.FolderPicker ??= () => PickFolderAsync("Select the VaM install folder");
            vm.DataFolderPicker ??= () => PickFolderAsync("Select the VarVault data folder");
            vm.ImportTempFolderPicker ??= () => PickFolderAsync("Select the archive temp folder");
            vm.SevenZipFilePicker ??= PickSevenZipAsync;
        }
    }

    private async System.Threading.Tasks.Task<string?> PickFolderAsync(string title)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        }).ConfigureAwait(true);
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    private async System.Threading.Tasks.Task<string?> PickSevenZipAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select 7z.exe",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("7-Zip executable") { Patterns = ["7z.exe", "*.exe"] },
                FilePickerFileTypes.All,
            ],
        }).ConfigureAwait(true);
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
