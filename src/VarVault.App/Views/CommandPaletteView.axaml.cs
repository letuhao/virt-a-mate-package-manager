using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VarVault.App.Views;

/// <summary>Command-palette overlay (Ctrl-K). (24-checklist E9.)</summary>
public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView() => AvaloniaXamlLoader.Load(this);
}
