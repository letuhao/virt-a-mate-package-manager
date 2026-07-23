using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

public partial class EditMetaDialog : UserControl
{
    public EditMetaDialog() => AvaloniaXamlLoader.Load(this);

    private void OnNewDepKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not EditMetaViewModel vm)
            return;
        if (vm.AddDepCommand.CanExecute(null))
            vm.AddDepCommand.Execute(null);
        e.Handled = true;
    }
}
