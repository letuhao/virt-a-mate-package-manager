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
        var dialogs = new Services.DialogService();
        // Forward-declared so the launcher's toast can reach the shell once it's built.
        ShellViewModel? shellRef = null;
        var launcher = new Services.DialogLauncher(services, dialogs,
            afterRepoAdded: () => EnqueueIndexAll(services),
            toast: (msg, undo) => shellRef?.ShowToast(msg, undo));

        var screens = new Dictionary<string, object>
        {
            ["library"] = new LibraryViewModel(
                services.GetRequiredService<ILibraryQueryService>(),
                services.GetService<Sdk.Settings.ISettingsService>(),
                actions: services.GetService<ILibraryActionService>(),
                launcher: launcher),
            ["analytics"] = new AnalyticsViewModel(services.GetRequiredService<IAnalyticsService>()),
            ["dashboard"] = new DashboardViewModel(services.GetRequiredService<IDashboardService>(), launcher),
            ["repos"] = new RepositoriesViewModel(services.GetRequiredService<Sdk.Repositories.IRepositoryService>(), launcher),
            ["presets"] = new PresetsViewModel(services.GetRequiredService<Sdk.Presets.IPresetService>(), services.GetService<IProfileService>(), launcher),
            ["tiering"] = new TieringViewModel(services.GetRequiredService<ITieringService>(), launcher),
            ["dupes"] = new DupesViewModel(services.GetRequiredService<IReclaimService>(), launcher),
            ["history"] = new ActivityViewModel(services.GetRequiredService<IActivityLog>()),
            ["missing"] = new MissingDepsViewModel(services.GetRequiredService<IMissingDepsQuery>(), launcher),
            ["proposals"] = new ProposalsViewModel(services.GetRequiredService<IProposalService>(), launcher),
            ["health"] = new HealthViewModel(services.GetRequiredService<IHealthService>(), launcher),
            ["trash"] = new TrashViewModel(services.GetRequiredService<ITrashQueryService>()),
            ["settings"] = new SettingsViewModel(services.GetRequiredService<Sdk.Settings.ISettingsService>()),
        };

        // Placeholders for screens whose full views arrive in SCR slices — keeps the shell complete.
        foreach (var s in ShellViewModel.AllScreens)
            screens.TryAdd(s.Id, new PlaceholderScreenViewModel(s.Label));

        var jobQueue = services.GetService<Sdk.Threading.IJobQueue>();
        var feeds = TryBuildFeeds(services, jobQueue);
        var shell = new ShellViewModel(screens, initial: "library", dialogs: dialogs, jobQueue: jobQueue, feeds: feeds);
        shellRef = shell; // wires the launcher toast callback above

        // GA-6 · top-bar handlers → open the matching dialog through the launcher.
        shell.AddRepoHandler = launcher.OpenAddRepo;
        shell.RescueHandler = () => { launcher.OpenRescue(); return System.Threading.Tasks.Task.CompletedTask; };

        // GD-1/GD-2 · let the dashboard + library navigate the shell.
        if (screens["dashboard"] is DashboardViewModel dash)
            dash.NavigateTo = shell.Navigate;
        if (screens["library"] is LibraryViewModel lib)
            lib.NavigateTo = shell.Navigate;

        return shell;
    }

    /// <summary>
    /// GA-5 · Enqueue a full index run on the job queue (BE-N0 orchestrator). Called at startup and after a
    /// repository is added, so the catalog actually populates — the runtime trigger the GUI was missing.
    /// Returns null when indexing isn't composed (minimal test hosts). (18-gap GA-5.)
    /// </summary>
    public static Sdk.Threading.JobHandle? EnqueueIndexAll(IServiceProvider services)
    {
        var queue = services.GetService<Sdk.Threading.IJobQueue>();
        var orchestrator = services.GetService<Sdk.Indexing.IIndexOrchestrator>();
        if (queue is null || orchestrator is null)
            return null;
        return queue.Enqueue("Indexing library", async ctx =>
        {
            var summary = await orchestrator.IndexAllAsync(ctx.Cancellation).ConfigureAwait(false);
            ctx.Progress.Report(new Common.ProgressReport(summary.Indexed, summary.Indexed,
                $"Indexed {summary.Indexed} vars across {summary.Repositories} repos"));
        });
    }

    /// <summary>Build the live-feeds source if all its read services are present (null in minimal test hosts).</summary>
    private static Services.IShellLiveFeeds? TryBuildFeeds(IServiceProvider services, Sdk.Threading.IJobQueue? jobQueue)
    {
        var proposals = services.GetService<IProposalService>();
        var health = services.GetService<IHealthService>();
        var missing = services.GetService<IMissingDepsQuery>();
        var dashboard = services.GetService<IDashboardService>();
        if (proposals is null || health is null || missing is null || dashboard is null || jobQueue is null)
            return null;
        return new Services.ShellLiveFeeds(proposals, health, missing, dashboard, jobQueue);
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
            var shell = CreateShell(scope.ServiceProvider);
            EnqueueIndexAll(scope.ServiceProvider); // GA-5: populate the catalog in the background on launch
            return shell;
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
