using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VarVault.App.Views;

/// <summary>SH-5 · Bottom log/status dock bound to the shell. (16-checklist SH-5.)</summary>
public partial class LogDockView : UserControl
{
    public LogDockView() => AvaloniaXamlLoader.Load(this);
}
