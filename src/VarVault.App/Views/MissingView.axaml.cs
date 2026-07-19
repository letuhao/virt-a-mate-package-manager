using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VarVault.App.Views;

/// <summary>SCR-10 · Missing deps screen. (16-checklist SCR-10.)</summary>
public partial class MissingView : UserControl
{
    public MissingView() => AvaloniaXamlLoader.Load(this);
}
