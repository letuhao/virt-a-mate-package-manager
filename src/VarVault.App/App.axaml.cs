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
            // Compose the backend host and resolve the shell. On composition failure show a real error window
            // (copyable message + detail), never a blank window bound to nothing. (24-checklist C1.)
            var (shell, error) = AppHost.TryCreateShellOrError();
            desktop.MainWindow = error is not null
                ? new StartupErrorWindow { DataContext = error }
                : new MainWindow { DataContext = shell };

            // Clean detach: tell the worker this owner is leaving and release the writer lease. (A12 liveness.)
            desktop.ShutdownRequested += (_, _) => IndexerClientOverride.ShutdownAsync().GetAwaiter().GetResult();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
