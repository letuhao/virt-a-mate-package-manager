using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VarVault.App.Views;

/// <summary>Interactive per-collision duplicate-entry fix picker.</summary>
public partial class DedupFixDialog : UserControl
{
    public DedupFixDialog() => AvaloniaXamlLoader.Load(this);
}
