using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VarVault.App.Views;

/// <summary>DLG-6 · Confirm-delete dialog. (16-checklist DLG-6.)</summary>
public partial class ConfirmDeleteDialog : UserControl
{
    public ConfirmDeleteDialog() => AvaloniaXamlLoader.Load(this);
}
