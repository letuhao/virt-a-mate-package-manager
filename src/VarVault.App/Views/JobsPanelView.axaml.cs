using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VarVault.App.Views;

/// <summary>SH-4 · Background jobs panel bound to the shell. (16-checklist SH-4.)</summary>
public partial class JobsPanelView : UserControl
{
    public JobsPanelView() => AvaloniaXamlLoader.Load(this);
}
