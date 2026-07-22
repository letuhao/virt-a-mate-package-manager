using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

/// <summary>DLG-10 · Var detail dialog. (16-checklist DLG-10.)</summary>
public partial class VarDetailDialog : UserControl
{
    public VarDetailDialog() => AvaloniaXamlLoader.Load(this);

    private void OnDependencyCardAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { DataContext: DependencyCardViewModel card })
            _ = card.LoadAsync();
    }
}
