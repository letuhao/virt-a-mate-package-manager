using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

/// <summary>DLG-2 · Add-repository dialog content. Wires the native folder picker for "Browse…". (16-checklist DLG-2.)</summary>
public partial class AddRepoDialog : UserControl
{
    public AddRepoDialog()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AddRepoViewModel { FolderPicker: null } vm)
                vm.FolderPicker = PickFolderAsync;
        };
    }

    private async Task<string?> PickFolderAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the repository folder",
            AllowMultiple = false,
        }).ConfigureAwait(true);
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}
