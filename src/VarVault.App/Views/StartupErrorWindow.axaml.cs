using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace VarVault.App.Views;

/// <summary>Minimal window shown when app composition fails at startup. (24-checklist C1.)</summary>
public partial class StartupErrorWindow : Window
{
    public StartupErrorWindow() => AvaloniaXamlLoader.Load(this);

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
