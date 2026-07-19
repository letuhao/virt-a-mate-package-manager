using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VarVault.App.Views;

/// <summary>SH-3 · Top bar bound to the shell. (16-checklist SH-3.)</summary>
public partial class TopBarView : UserControl
{
    public TopBarView() => AvaloniaXamlLoader.Load(this);
}
