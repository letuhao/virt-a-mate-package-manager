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
                launcher: launcher,
                detail: services.GetService<IPackageDetailQuery>(),
                presets: services.GetService<Sdk.Presets.IPresetService>(),
                tags: services.GetService<ITagService>(),
                thumbnails: services.GetService<Domain.Indexing.IThumbnailStore>(),
                reveal: new Services.FileReveal(),
                activation: services.GetService<Sdk.Activation.IActivationService>()),
            ["analytics"] = new AnalyticsViewModel(services.GetRequiredService<IAnalyticsService>(), services.GetService<ITieringService>()),
            ["dashboard"] = new DashboardViewModel(services.GetRequiredService<IDashboardService>(), launcher,
                services.GetService<IReclaimService>(), services.GetService<IActivityLog>()),
            ["repos"] = new RepositoriesViewModel(services.GetRequiredService<Sdk.Repositories.IRepositoryService>(), launcher),
            ["presets"] = new PresetsViewModel(services.GetRequiredService<Sdk.Presets.IPresetService>(), services.GetService<IProfileService>(), launcher, services.GetService<Sdk.Activation.IActivationService>(), services.GetService<Sdk.Settings.ISettingsService>()),
            ["tiering"] = new TieringViewModel(services.GetRequiredService<ITieringService>(), launcher),
            ["dupes"] = new DupesViewModel(services.GetRequiredService<IReclaimService>(), launcher, services.GetService<IIntakeService>()),
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
        {
            lib.NavigateTo = shell.Navigate;
            lib.ShowToast = shell.ShowToast;

            // GA-4/AC-7 · log-dock "selected N" tracks the library selection when it is the active screen.
            lib.SelectedItems.CollectionChanged += (_, _) =>
            {
                if (ReferenceEquals(shell.ActiveScreen, lib))
                    shell.SelectedCount = lib.SelectedItems.Count;
            };
            shell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ShellViewModel.ActiveScreen))
                    shell.SelectedCount = ReferenceEquals(shell.ActiveScreen, lib) ? lib.SelectedItems.Count : 0;
            };

            // AC-8 · top-bar search Enter → apply the text as the library filter and navigate there.
            shell.SearchHandler = text =>
            {
                lib.SearchText = string.IsNullOrWhiteSpace(text) ? null : text;
                shell.Navigate("library");
            };
        }

        // E9 · command palette (Ctrl-K): jump to any screen + a couple of quick actions.
        var paletteCommands = ShellViewModel.AllScreens
            .Select(s => new PaletteCommand($"Go to {s.Label}", s.Group, () => shell.Navigate(s.Id)))
            .ToList();
        paletteCommands.Add(new PaletteCommand("Add repository…", "Actions", () => launcher.OpenAddRepo()));
        paletteCommands.Add(new PaletteCommand("Rescue…", "Actions", () => launcher.OpenRescue()));
        shell.CommandPalette = new CommandPaletteViewModel(paletteCommands);

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

    /// <summary>Reconcile Profile rows with on-disk profile dirs at launch (derive active, prune vanished). (T3.3)</summary>
    private static void ReconcileProfiles(IServiceProvider rootServices)
    {
        // Fire-and-forget on a dedicated scope so we never share the shell's DbContext across threads.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = rootServices.CreateScope();
                var activation = scope.ServiceProvider.GetService<Sdk.Activation.IActivationService>();
                if (activation is not null)
                    await activation.ReconcileProfilesAsync().ConfigureAwait(false);
            }
            catch (Exception) { /* best-effort at startup */ }
        });
    }

    /// <summary>Build the live-feeds source if all its read services are present (null in minimal test hosts).</summary>
    private static Services.IShellLiveFeeds? TryBuildFeeds(IServiceProvider services, Sdk.Threading.IJobQueue? jobQueue)
    {
        // Presence check keeps minimal test hosts (without these read services) returning null; the feeds resolve
        // their services per-poll from a fresh scope, so the poll never shares the shell's DbContext. (24-checklist B1.)
        var scopeFactory = services.GetService<IServiceScopeFactory>();
        if (services.GetService<IProposalService>() is null || services.GetService<IHealthService>() is null
            || services.GetService<IMissingDepsQuery>() is null || services.GetService<IDashboardService>() is null
            || jobQueue is null || scopeFactory is null)
            return null;
        return new Services.ShellLiveFeeds(scopeFactory, jobQueue);
    }

    private static string DefaultDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VarVault");

    /// <summary>
    /// Compose the app host + shell, or return a startup-error view-model on failure — never a null result that
    /// would render as a blank window bound to nothing. The exception is traced and surfaced (copyable). (24-checklist C1.)
    /// </summary>
    public static (ShellViewModel? Shell, StartupErrorViewModel? Error) TryCreateShellOrError() =>
        TryCreateShellOrError(DefaultDataDir);

    /// <summary>Testable overload: compose under an explicit data directory.</summary>
    public static (ShellViewModel? Shell, StartupErrorViewModel? Error) TryCreateShellOrError(string dataDir)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            var host = Bootstrap.BuildApp(dataDir);
            var scope = host.Services.CreateScope(); // app-lifetime scope backing the shell's read services
            var shell = CreateShell(scope.ServiceProvider);
            ReconcileProfiles(host.Services); // T3.3: sync Profile rows with on-disk profile dirs (own scope)
            EnqueueIndexAll(scope.ServiceProvider); // GA-5: populate the catalog in the background on launch
            return (shell, null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("VarVault startup failed: " + ex);
            return (null, new StartupErrorViewModel("VarVault couldn't start.", ex.ToString()));
        }
    }

    /// <summary>Back-compat: the shell only (null on failure). Prefer <see cref="TryCreateShellOrError()"/>.</summary>
    public static ShellViewModel? TryCreateShell() => TryCreateShellOrError().Shell;

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
