using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

/// <summary>Import &amp; review screen. Handles the review keyboard map (J/K, ] [ \, Del). (doc 31 Phase 6.2/6.6.)</summary>
public partial class ImportView : UserControl
{
    public ImportView()
    {
        AvaloniaXamlLoader.Load(this);
        // Tunnel so the mapped keys win before the ListBox's type-ahead search / Del handling. (6.6)
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Review shortcuts from the UX draft: J/↓ &amp; K/↑ navigate the list, ] [ \ set the primary/secondary/both
    /// decision on the selected review item, Del/Backspace discard it. Text-entry controls keep their own keys.
    /// (6.6.)
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ImportViewModel vm)
            return;
        if (e.Source is TextBox or ComboBox)   // don't hijack typing/filtering
            return;

        switch (e.Key)
        {
            case Key.J or Key.Down: vm.MoveSelection(+1); break;
            case Key.K or Key.Up: vm.MoveSelection(-1); break;
            case Key.OemCloseBrackets: vm.DecidePrimary(); break;   // ]
            case Key.OemOpenBrackets: vm.DecideSecondary(); break;  // [
            case Key.OemPipe or Key.OemBackslash: vm.DecideBoth(); break; // \
            case Key.Delete or Key.Back: vm.DiscardSelected(); break;
            default: return;
        }
        e.Handled = true;
    }
}
