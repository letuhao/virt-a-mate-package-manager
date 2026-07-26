using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using VarVault.App.Services;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

/// <summary>SCR-10 · Missing deps screen. (16-checklist SCR-10.)</summary>
public partial class MissingView : UserControl
{
    public MissingView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => WirePickers();
        AttachedToVisualTree += (_, _) => WirePickers();
        WirePickers();
    }

    private void WirePickers()
    {
        if (DataContext is not MissingDepsViewModel vm)
            return;
        vm.SaveTxtPicker = name => UiStoragePickers.SaveTxtAsync(this, name, "Export missing links");
    }
}
