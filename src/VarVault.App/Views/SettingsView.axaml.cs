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
            vm.FolderPicker = PickFolderAsync;
    }

    private async System.Threading.Tasks.Task<string?> PickFolderAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the VaM install folder",
            AllowMultiple = false,
        }).ConfigureAwait(true);
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}
