using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.ViewModels;
using VarVault.Host;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;

namespace VarVault.App.Composition;

/// <summary>
/// Composes the backend host and the shell view-model. Depends only on Host + SDK. Screen view-models are
/// resolved from SDK services; screens whose views land in later SCR slices get a placeholder so the shell
/// always resolves with all 13 screens non-null. (16-checklist SH-6.)
/// </summary>
public static class AppHost
{
    /// <summary>Build the shell + all screen view-models from a composed service provider (app-lifetime scope).</summary>
    public static ShellViewModel CreateShell(IServiceProvider services)
    {
        var screens = new Dictionary<string, object>
        {
            ["library"] = new LibraryViewModel(
                services.GetRequiredService<ILibraryQueryService>(),
                services.GetService<Sdk.Settings.ISettingsService>(),
                actions: services.GetService<ILibraryActionService>()),
            ["analytics"] = new AnalyticsViewModel(services.GetRequiredService<IAnalyticsService>()),
            ["dashboard"] = new DashboardViewModel(services.GetRequiredService<IDashboardService>()),
            ["repos"] = new RepositoriesViewModel(services.GetRequiredService<Sdk.Repositories.IRepositoryService>()),
            ["presets"] = new PresetsViewModel(services.GetRequiredService<Sdk.Presets.IPresetService>(), services.GetService<IProfileService>()),
            ["tiering"] = new TieringViewModel(services.GetRequiredService<ITieringService>()),
            ["dupes"] = new DupesViewModel(services.GetRequiredService<IReclaimService>()),
            ["history"] = new ActivityViewModel(services.GetRequiredService<IActivityLog>()),
            ["missing"] = new MissingDepsViewModel(services.GetRequiredService<IMissingDepsQuery>()),
            ["proposals"] = new ProposalsViewModel(services.GetRequiredService<IProposalService>()),
        };

        // Placeholders for screens whose full views arrive in SCR slices — keeps the shell complete.
        foreach (var s in ShellViewModel.AllScreens)
            screens.TryAdd(s.Id, new PlaceholderScreenViewModel(s.Label));

        return new ShellViewModel(screens, initial: "library");
    }

    /// <summary>Compose the app host under LocalAppData and build the shell; null on composition failure.</summary>
    public static ShellViewModel? TryCreateShell()
    {
        try
        {
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VarVault");
            Directory.CreateDirectory(dataDir);
            var host = Bootstrap.BuildApp(dataDir);
            var scope = host.Services.CreateScope(); // app-lifetime scope backing the shell's read services
            return CreateShell(scope.ServiceProvider);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Legacy library-only view-model (kept for the existing MainWindow until screens land).</summary>
    public static MainWindowViewModel? TryCreateMainViewModel()
    {
        try
        {
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VarVault");
            Directory.CreateDirectory(dataDir);
            var host = Bootstrap.BuildApp(dataDir);
            var scope = host.Services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<ILibraryQueryService>();
            return new MainWindowViewModel(new LibraryViewModel(library));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
