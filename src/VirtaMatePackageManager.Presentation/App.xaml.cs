using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VirtaMatePackageManager.Presentation.Services;

namespace VirtaMatePackageManager.Presentation;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize DI container
        _ = ServiceProviderFactory.ServiceProvider;

        // Get MainWindow from DI and show it
        var mainWindow = ServiceProviderFactory.ServiceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Clean up DI container
        ServiceProviderFactory.Dispose();
        
        base.OnExit(e);
    }
}

