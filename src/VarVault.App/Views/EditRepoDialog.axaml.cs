using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

/// <summary>Edit-repository dialog. Wires the native folder picker for the "Browse…" re-point.</summary>
public partial class EditRepoDialog : UserControl
{
    public EditRepoDialog()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is EditRepoViewModel { FolderPicker: null } vm)
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
