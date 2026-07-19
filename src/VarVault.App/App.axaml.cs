using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VarVault.App.Composition;
using VarVault.App.Views;

namespace VarVault.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Compose the backend host and resolve the main view-model. Kept resilient: a
            // composition failure still shows a window rather than crashing on start.
            desktop.MainWindow = new MainWindow
            {
                DataContext = AppHost.TryCreateMainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
