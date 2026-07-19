using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VarVault.App.Views;

/// <summary>DLG-3 · Migration plan dialog. (16-checklist DLG-3.)</summary>
public partial class MigrateDialog : UserControl
{
    public MigrateDialog() => AvaloniaXamlLoader.Load(this);
}
