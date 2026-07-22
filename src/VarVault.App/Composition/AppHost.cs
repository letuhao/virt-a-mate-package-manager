using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VarVault.App.Services;
using VarVault.App.ViewModels;
using VarVault.Host;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;
using VarVault.Sdk.Threading;

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

        var importJobs = services.GetService<ImportJobRunner>()
            ?? new ImportJobRunner(
                services.GetRequiredService<Sdk.Threading.IJobQueue>(),
                services.GetRequiredService<IServiceScopeFactory>());
        var uiDispatcher = services.GetRequiredService<IUiDispatcher>();
        // One shared runner for Library / Proposals / Fix dialog — never resolve a second orphan instance.
        var encodingJobs = services.GetService<EncodingFixJobRunner>()
            ?? new EncodingFixJobRunner(
                services.GetRequiredService<Sdk.Threading.IJobQueue>(),
                services.GetRequiredService<IServiceScopeFactory>(),
                uiDispatcher);
        var installedJobs = services.GetService<InstalledDepsRepairJobRunner>()
            ?? new InstalledDepsRepairJobRunner(
                services.GetRequiredService<Sdk.Threading.IJobQueue>(),
                services.GetRequiredService<IServiceScopeFactory>(),
                uiDispatcher);

        var launcher = new Services.DialogLauncher(services, dialogs,
            afterRepoAdded: () => EnqueueIndexAll(services),
            toast: (msg, undo) => shellRef?.ShowToast(msg, undo),
            encodingJobs: encodingJobs);

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
                activation: services.GetService<Sdk.Activation.IActivationService>(),
                indexer: IndexerClientOverride.Current ?? services.GetService<Sdk.Indexer.IIndexerClient>(),
                encodingJobs: encodingJobs),
            ["analytics"] = new AnalyticsViewModel(services.GetRequiredService<IAnalyticsService>(), services.GetService<ITieringService>()),
            ["dashboard"] = new DashboardViewModel(services.GetRequiredService<IDashboardService>(), launcher,
                services.GetService<IReclaimService>(), services.GetService<IActivityLog>()),
            ["repos"] = new RepositoriesViewModel(services.GetRequiredService<Sdk.Repositories.IRepositoryService>(), launcher),
            ["presets"] = new PresetsViewModel(services.GetRequiredService<Sdk.Presets.IPresetService>(), services.GetService<IProfileService>(), launcher, services.GetService<Sdk.Activation.IActivationService>(), services.GetService<Sdk.Settings.ISettingsService>()),
            ["tiering"] = new TieringViewModel(services.GetRequiredService<ITieringService>(), launcher, services.GetService<Sdk.Repositories.IRepositoryService>()),
            ["dupes"] = new DupesViewModel(services.GetRequiredService<IReclaimService>(), launcher),
            ["import"] = new ImportViewModel(
                services.GetRequiredService<Sdk.Import.IImportService>(),
                services.GetRequiredService<Sdk.Repositories.IRepositoryService>(),
                importJobs,
                uiDispatcher,
                services.GetService<Sdk.Import.IAddonPackagesLooseVarsLocator>(),
                services.GetService<Domain.Safety.ITrashService>()),
            ["history"] = new ActivityViewModel(services.GetRequiredService<IActivityLog>()),
            ["missing"] = new MissingDepsViewModel(
                services.GetRequiredService<IMissingDepsQuery>(),
                launcher,
                services.GetService<IMissingLogResolver>(),
                services.GetService<IInstalledDepsRepair>(),
                installedJobs,
                services.GetService<IClipboard>() ?? new AvaloniaClipboard()),
            ["proposals"] = new ProposalsViewModel(services.GetRequiredService<IProposalService>(), launcher, encodingJobs),
            ["health"] = new HealthViewModel(services.GetRequiredService<IHealthService>(), launcher),
            ["trash"] = new TrashViewModel(services.GetRequiredService<ITrashQueryService>()),
            ["settings"] = new SettingsViewModel(services.GetRequiredService<Sdk.Settings.ISettingsService>()),
        };

        // Placeholders for screens whose full views arrive in SCR slices — keeps the shell complete.
        foreach (var s in ShellViewModel.AllScreens)
            screens.TryAdd(s.Id, new PlaceholderScreenViewModel(s.Label));

        var jobQueue = services.GetService<Sdk.Threading.IJobQueue>();
        var feeds = TryBuildFeeds(services, jobQueue);
        var shell = new ShellViewModel(screens, initial: "dashboard", dialogs: dialogs, jobQueue: jobQueue, feeds: feeds);
        shellRef = shell; // wires the launcher toast callback above

        encodingJobs.ShowToast = msg => shell.ShowToast(msg, null);
        encodingJobs.AfterCompleted = () =>
        {
            if (screens["health"] is HealthViewModel healthVm)
                _ = healthVm.LoadAsync();
            if (screens["library"] is LibraryViewModel libraryVm)
                _ = libraryVm.RefreshAsync();
        };

        installedJobs.ShowToast = msg => shell.ShowToast(msg, null);
        // Library only — MissingDepsViewModel applies leftovers then refreshes itself after await.
        installedJobs.AfterCompleted = () =>
        {
            if (screens["library"] is LibraryViewModel libraryVm)
                _ = libraryVm.RefreshAsync();
        };

        // Import rail badge = the live review-lane count of the Import screen's current session (spec §10).
        if (screens["import"] is ImportViewModel importVm)
            importVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ImportViewModel.ReviewRemaining))
                    shell.SetBadge("import", importVm.ReviewRemaining == 0 ? null : importVm.ReviewRemaining);
            };

        // GA-6 · top-bar handlers → open the matching dialog through the launcher.
        shell.AddRepoHandler = launcher.OpenAddRepo;
        shell.RescueHandler = () => { launcher.OpenRescue(); return System.Threading.Tasks.Task.CompletedTask; };
        // C1.2 · first-run onboarding opens through the same launcher (the window triggers it on load).
        shell.OnboardingHandler = launcher.OpenOnboarding;

        if (screens["repos"] is RepositoriesViewModel reposVm)
        {
            reposVm.ShowToast = shell.ShowToast;
            // Manual re-index is an explicit "force full" — bypass the unchanged-repo fast-path. (A16.)
            reposVm.ReindexRepo = id => EnqueueIndexRepo(services, id, forceFull: true);
            reposVm.ReindexAll = () => EnqueueIndexAll(services, forceFull: true);
        }

        // GD-1/GD-2 · let the dashboard + library navigate the shell.
        if (screens["dashboard"] is DashboardViewModel dash)
            dash.NavigateTo = shell.Navigate;
        if (screens["library"] is LibraryViewModel lib)
        {
            lib.NavigateTo = shell.Navigate;
            lib.ShowToast = shell.ShowToast;

            // GA-4/AC-7 · log-dock "selected N" tracks the library selection when it is the active screen.
            lib.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LibraryViewModel.SelectedCount) && ReferenceEquals(shell.ActiveScreen, lib))
                    shell.SelectedCount = lib.SelectedCount;
            };
            lib.SelectedItems.CollectionChanged += (_, _) =>
            {
                if (ReferenceEquals(shell.ActiveScreen, lib))
                    shell.SelectedCount = lib.SelectedCount;
            };
            shell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ShellViewModel.ActiveScreen))
                    shell.SelectedCount = ReferenceEquals(shell.ActiveScreen, lib) ? lib.SelectedCount : 0;
            };

            // AC-8 · top-bar search Enter → apply the text as the library filter and navigate there.
            shell.SearchHandler = text =>
            {
                lib.SearchText = string.IsNullOrWhiteSpace(text) ? null : text;
                shell.Navigate("library");
            };
            // Typing in the top bar while already on Library should filter live (same as the in-page box).
            shell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(ShellViewModel.SearchText))
                    return;
                if (!ReferenceEquals(shell.ActiveScreen, lib))
                    return;
                lib.SearchText = string.IsNullOrWhiteSpace(shell.SearchText) ? null : shell.SearchText;
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
    /// Enqueue a full index via the indexer worker (coalesced). The job queue entry mirrors worker
    /// progress into the jobs panel. (A12; GA-5.)
    /// </summary>
    public static Sdk.Threading.JobHandle? EnqueueIndexAll(IServiceProvider services, bool forceFull = false)
    {
        var queue = services.GetService<Sdk.Threading.IJobQueue>();
        var indexer = IndexerClientOverride.Current ?? services.GetService<Sdk.Indexer.IIndexerClient>();
        if (queue is null)
            return null;

        // Prefer worker client; fall back to in-proc orchestrator for minimal test hosts.
        if (indexer is not null)
        {
            return queue.Enqueue(forceFull ? "Re-indexing library (full)" : "Indexing library", async ctx =>
            {
                var start = await indexer.StartIndexAllAsync(forceFull, CancellationToken.None).ConfigureAwait(false);
                if (start.IsFailure)
                    throw new InvalidOperationException(start.Error.Message);
                await MirrorIndexerAsync(indexer, start.Value, ctx).ConfigureAwait(false);
            });
        }

        var orchestrator = services.GetService<Sdk.Indexing.IIndexOrchestrator>();
        if (orchestrator is null)
            return null;
        return queue.Enqueue("Indexing library", async ctx =>
        {
            var summary = await orchestrator.IndexAllAsync(ctx.Progress, ctx.Cancellation).ConfigureAwait(false);
            ctx.Progress.Report(new Common.ProgressReport(summary.Indexed, Math.Max(1, summary.Indexed),
                $"Indexed {summary.Indexed} vars across {summary.Repositories} repos"));
        });
    }

    public static Sdk.Threading.JobHandle? EnqueueIndexRepo(IServiceProvider services, System.Guid repositoryId, bool forceFull = false)
    {
        var queue = services.GetService<Sdk.Threading.IJobQueue>();
        var indexer = IndexerClientOverride.Current ?? services.GetService<Sdk.Indexer.IIndexerClient>();
        if (queue is null)
            return null;

        if (indexer is not null)
        {
            return queue.Enqueue("Re-indexing repository", async ctx =>
            {
                var start = await indexer.StartIndexRepositoryAsync(repositoryId, forceFull, CancellationToken.None).ConfigureAwait(false);
                if (start.IsFailure)
                    throw new InvalidOperationException(start.Error.Message);
                await MirrorIndexerAsync(indexer, start.Value, ctx).ConfigureAwait(false);
            });
        }

        var orchestrator = services.GetService<Sdk.Indexing.IIndexOrchestrator>();
        if (orchestrator is null)
            return null;
        return queue.Enqueue("Re-indexing repository", async ctx =>
        {
            var summary = await orchestrator.IndexRepositoryAsync(repositoryId, ctx.Progress, ctx.Cancellation).ConfigureAwait(false);
            ctx.Progress.Report(new Common.ProgressReport(summary.Indexed, Math.Max(1, summary.Indexed),
                $"Indexed {summary.Indexed} vars ({summary.Skipped} unchanged, {summary.Pruned} pruned)"));
        });
    }

    private static async System.Threading.Tasks.Task MirrorIndexerAsync(
        Sdk.Indexer.IIndexerClient indexer, System.Guid jobId, Sdk.Threading.JobContext ctx)
    {
        try
        {
            while (!ctx.Cancellation.IsCancellationRequested)
            {
                var status = await indexer.GetStatusAsync(ctx.Cancellation).ConfigureAwait(false);
                if (status.IsFailure)
                    break;
                var s = status.Value;
                ctx.Progress.Report(new Common.ProgressReport(
                    s.Done, Math.Max(1, s.Total),
                    s.PhaseMessage ?? s.State.ToString()));
                if (s.State is Sdk.Indexer.IndexerJobState.Completed
                    or Sdk.Indexer.IndexerJobState.Failed
                    or Sdk.Indexer.IndexerJobState.Cancelled
                    or Sdk.Indexer.IndexerJobState.Idle)
                {
                    if (s.State == Sdk.Indexer.IndexerJobState.Failed && s.Error is not null)
                        throw new InvalidOperationException(s.Error);
                    break;
                }
                await System.Threading.Tasks.Task.Delay(400, ctx.Cancellation).ConfigureAwait(false);
            }

            ctx.Cancellation.ThrowIfCancellationRequested();
        }
        catch (System.OperationCanceledException)
        {
            // GUI job cancelled → cancel the worker job (in-proc or out-of-process).
            try { await indexer.CancelAsync(jobId, CancellationToken.None).ConfigureAwait(false); }
            catch { /* best-effort */ }
            throw;
        }
    }

    private const string OnboardingSeenKey = "onboarding.seen";

    /// <summary>
    /// C1.1/C1.3 · First-run detection: true when no repositories are registered AND the onboarding wizard hasn't
    /// been dismissed before. Uses its own scope (never shares the shell's DbContext). Persists the "seen" flag so a
    /// user who dismisses the wizard without adding a repo isn't nagged on every launch. Returns false on any error
    /// (never block startup on this). (28-checklist C1.)
    /// </summary>
    public static async Task<bool> NeedsOnboardingAsync(IServiceProvider rootServices)
    {
        try
        {
            using var scope = rootServices.CreateScope();
            var repos = scope.ServiceProvider.GetService<Sdk.Repositories.IRepositoryService>();
            if (repos is null)
                return false;
            var list = await repos.ListAsync().ConfigureAwait(false);
            if (list.Count > 0)
                return false; // already set up
            var settings = scope.ServiceProvider.GetService<Sdk.Settings.ISettingsService>();
            if (settings is null)
                return true;
            var seen = await settings.GetBoolAsync(OnboardingSeenKey, false).ConfigureAwait(false);
            if (!seen)
                await settings.SetBoolAsync(OnboardingSeenKey, true).ConfigureAwait(false); // show once
            return !seen;
        }
        catch (Exception)
        {
            return false; // best-effort — a detection failure must never block launch
        }
    }

    /// <summary>Sweep import temp-workspace dirs orphaned by a crash, at launch (own scope, best-effort). (§6/E5)</summary>
    private static void SweepImportTemp(IServiceProvider rootServices)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = rootServices.CreateScope();
                var import = scope.ServiceProvider.GetService<Sdk.Import.IImportService>();
                if (import is not null)
                    await import.SweepTempWorkspacesAsync().ConfigureAwait(false);
            }
            catch (Exception) { /* best-effort at startup */ }
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
        var scopeFactory = services.GetService<IServiceScopeFactory>();
        if (services.GetService<IDashboardService>() is null || scopeFactory is null)
            return null;
        var indexer = IndexerClientOverride.Current ?? services.GetService<Sdk.Indexer.IIndexerClient>();
        return new Services.ShellLiveFeeds(scopeFactory, indexer);
    }

    private static string DefaultDataDir => AppDataLocation.Resolve();

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
            var host = Bootstrap.BuildApp(dataDir, services =>
            {
                services.RemoveAll<IUiDispatcher>();
                services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
                services.AddSingleton<ImportJobRunner>();
                services.AddSingleton<EncodingFixJobRunner>();
                services.AddSingleton<InstalledDepsRepairJobRunner>();
            });
            var scope = host.Services.CreateScope(); // app-lifetime scope backing the shell's read services
            IndexerClientOverride.Current = IndexerProcessHost.ResolveClient(host.Services, dataDir);
            // Keep a worker reachable for the whole session (respawn/reconnect if it dies). (A12 liveness.)
            IndexerClientOverride.Monitor = new IndexerHealthMonitor(host.Services, dataDir);
            IndexerClientOverride.Monitor.Start();
            var shell = CreateShell(scope.ServiceProvider);
            // C1.1 · a zero-repository install (that hasn't dismissed the wizard) opens onboarding on load.
            shell.ShowOnboardingOnLoad = NeedsOnboardingAsync(host.Services).GetAwaiter().GetResult();
            ReconcileProfiles(host.Services); // T3.3: sync Profile rows with on-disk profile dirs (own scope)
            SweepImportTemp(host.Services);   // §6/E5: remove import temp dirs orphaned by a crash (own scope)
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
            var dataDir = AppDataLocation.Resolve();
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
