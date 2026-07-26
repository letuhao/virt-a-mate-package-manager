using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using VarVault.App.Services;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

/// <summary>DLG-7 · Preset-edit dialog. (16-checklist DLG-7.)</summary>
public partial class PresetEditDialog : UserControl
{
    public PresetEditDialog()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => WirePickers();
        AttachedToVisualTree += (_, _) => WirePickers();
        WirePickers();
    }

    private void WirePickers()
    {
        if (DataContext is not PresetEditViewModel vm)
            return;
        vm.SaveTxtPicker = name => UiStoragePickers.SaveTxtAsync(this, name, "Export preset members");
    }
}
