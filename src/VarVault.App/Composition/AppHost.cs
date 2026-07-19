using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.ViewModels;
using VarVault.Host;
using VarVault.Sdk.Library;

namespace VarVault.App.Composition;

/// <summary>
/// Composes the backend host for the desktop app (built-in modules + catalog DB under LocalAppData)
/// and resolves the root view-model. Isolated here so the app shell stays declarative, and depends
/// only on the Host + SDK (never Infrastructure directly).
/// </summary>
public static class AppHost
{
    public static MainWindowViewModel? TryCreateMainViewModel()
    {
        try
        {
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VarVault");
            Directory.CreateDirectory(dataDir);

            var host = Bootstrap.BuildApp(dataDir);

            // A single app-lifetime scope backs the read services the view-models query.
            var scope = host.Services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<ILibraryQueryService>();
            return new MainWindowViewModel(new LibraryViewModel(library));
        }
        catch (Exception)
        {
            return null; // shell still renders; the user sees an empty state
        }
    }
}
