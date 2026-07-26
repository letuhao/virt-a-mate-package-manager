using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using VarVault.App.Services;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

/// <summary>SCR-4 · Loading presets screen. (16-checklist SCR-4.)</summary>
public partial class PresetsView : UserControl
{
    public PresetsView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => WirePickers();
        AttachedToVisualTree += (_, _) => WirePickers();
        WirePickers();
    }

    private void WirePickers()
    {
        if (DataContext is not PresetsViewModel vm)
            return;
        vm.OpenTxtPicker = () => UiStoragePickers.OpenTxtAsync(this, "Import preset members");
        vm.SaveTxtPicker = name => UiStoragePickers.SaveTxtAsync(this, name, "Export preset members");
    }
}
