using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtaMatePackageManager.Infrastructure.DependencyInjection;
using System.IO;

namespace VirtaMatePackageManager.Presentation.Services;

/// <summary>
/// Factory for creating and configuring the service provider.
/// </summary>
public static class ServiceProviderFactory
{
    private static IServiceProvider? _serviceProvider;

    /// <summary>
    /// Gets or creates the service provider instance.
    /// </summary>
    public static IServiceProvider ServiceProvider
    {
        get
        {
            if (_serviceProvider == null)
            {
                _serviceProvider = CreateServiceProvider();
            }
            return _serviceProvider;
        }
    }

    /// <summary>
    /// Creates and configures the service provider.
    /// </summary>
    public static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        // Build configuration
        var configuration = BuildConfiguration();

        // Register all VirtaMate Package Manager services
        services.AddVirtaMatePackageManager(configuration);

        // Register Presentation layer ViewModels
        services.AddTransient<ViewModels.MainWindowViewModel>();
        services.AddTransient<ViewModels.RepositoryManagementViewModel>();
        services.AddTransient<ViewModels.InstallationTargetManagementViewModel>();
        services.AddTransient<ViewModels.VarPackageDetailsViewModel>();
        services.AddTransient<ViewModels.ScanProgressViewModel>();
        services.AddTransient<ViewModels.BackendSettingsViewModel>();

        // Register Presentation layer dialogs and windows
        services.AddTransient<MainWindow>();
        services.AddTransient<Dialogs.RepositoryManagementDialog>();
        services.AddTransient<Dialogs.InstallationTargetManagementDialog>();
        services.AddTransient<Dialogs.VarPackageDetailsDialog>();
        services.AddTransient<Dialogs.BackendSettingsDialog>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds the configuration from appsettings.json.
    /// </summary>
    private static IConfiguration BuildConfiguration()
    {
        var builder = new ConfigurationBuilder();

        // Get the directory where the executable is located
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        
        // Try to find appsettings.json in multiple locations
        var possiblePaths = new[]
        {
            Path.Combine(basePath, "appsettings.json"),
            Path.Combine(basePath, "..", "..", "..", "..", "VirtaMatePackageManager.Infrastructure", "appsettings.json"),
            Path.Combine(basePath, "..", "..", "..", "..", "src", "VirtaMatePackageManager.Infrastructure", "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "VirtaMatePackageManager.Infrastructure", "appsettings.json")
        };

        string? appsettingsPath = null;
        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                appsettingsPath = fullPath;
                break;
            }
        }

        if (appsettingsPath != null)
        {
            builder.AddJsonFile(appsettingsPath, optional: false, reloadOnChange: true);
        }
        else
        {
            // If appsettings.json not found, use environment variables or defaults
            System.Diagnostics.Debug.WriteLine("Warning: appsettings.json not found. Using default configuration.");
            
            // Use environment variable for connection string if available
            var connectionString = Environment.GetEnvironmentVariable("VIRTAMATE_DB_CONNECTION_STRING")
                ?? "Host=localhost;Port=5432;Database=virtamate_package_manager;Username=postgres;Password=postgres";
            
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:DefaultConnection", connectionString }
            });
        }

        // Environment variables will be read via configuration if needed
        // builder.AddEnvironmentVariables() requires additional package

        return builder.Build();
    }

    /// <summary>
    /// Disposes the service provider and cleans up resources.
    /// </summary>
    public static void Dispose()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
            _serviceProvider = null;
        }
    }
}

