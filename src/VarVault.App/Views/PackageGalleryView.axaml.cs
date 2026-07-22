using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

/// <summary>Shared package content gallery (wall + focus) for Library sidebar and Var detail.</summary>
public partial class PackageGalleryView : UserControl
{
    public PackageGalleryView()
    {
        AvaloniaXamlLoader.Load(this);
        Focusable = true;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnThumbAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { DataContext: GalleryThumbViewModel thumb })
            _ = thumb.LoadAsync();
    }

    private void OnThumbClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PackageGalleryViewModel vm)
            return;
        if (sender is Control { DataContext: GalleryThumbViewModel thumb })
            _ = vm.EnterFocusCommand.ExecuteAsync(thumb);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not PackageGalleryViewModel vm || !vm.IsFocusMode)
            return;

        switch (e.Key)
        {
            case Key.Escape:
                vm.ExitFocusCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                _ = vm.FocusPreviousCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case Key.Right:
                _ = vm.FocusNextCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case Key.Home:
                _ = vm.FocusFirstCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case Key.End:
                _ = vm.FocusLastCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
        }
    }
}
