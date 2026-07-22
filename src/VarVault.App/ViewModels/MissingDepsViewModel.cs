using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;

namespace VarVault.App.ViewModels;

/// <summary>Missing-deps screen: unresolved refs with needed-by counts, VaM-log repair, and installed-set
/// dependency repair (legacy Installed Packages / MissingDepends). (Checklist 2.14; QoL.)</summary>
public sealed partial class MissingDepsViewModel(
    IMissingDepsQuery query,
    Services.IDialogLauncher? launcher = null,
    IMissingLogResolver? logResolver = null,
    IInstalledDepsRepair? installedRepair = null,
    Services.InstalledDepsRepairJobRunner? installedJobs = null)
    : ObservableObject, ILoadableScreen
{
    public PagedListState<MissingDependency> Pager { get; } =
        new((request, ct) => query.GetPageAsync(request, cancellationToken: ct));

    /// <summary>Rows currently rendered (capped subset unless <see cref="ShowAll"/>). </summary>
    public ObservableCollection<MissingDependency> Items => Pager.Items;

    public bool IsEmpty => Pager.IsEmpty;

    /// <summary>Total distinct referenced-but-absent packages (the full set, not the capped view). (D1.1)</summary>
    public int TotalMissing => Pager.TotalCount;
    public bool IsCapped => false;
    public bool CanToggle => false;
    public string CapLabel => string.Empty;
    public string ToggleLabel => "Show all";

    /// <summary>One-line framing so the number has context and the newcomer knows these are downloads, not a bug. (D1.1/D1.3)</summary>
    public string HeaderSummary => Pager.TotalCount == 0
        ? "No missing dependencies — every referenced package is in your library."
        : $"{Pager.TotalCount} packages are referenced by your library but aren't in it — downloads you still need. "
          + "VarVault can't create these; use “Export links txt” to fetch them, or “Resolve” to map a ref to a package you do have.";

    /// <summary>Resolve/Edit-alias → alias dialog for the missing ref. (GD-12)</summary>
    [RelayCommand]
    private void Resolve(MissingDependency dep)
    {
        if (dep is not null)
            launcher?.OpenAlias(dep.Ref, onSaved: () => _ = RefreshAsync());
    }

    /// <summary>Resolve a leftover from installed-deps analyze (optional suggested owned var). </summary>
    [RelayCommand]
    private void ResolveInstalledLeftover(InstalledDepsEntry? entry)
    {
        if (entry is null)
            return;
        launcher?.OpenAlias(
            entry.Ref,
            onSaved: () => _ = RefreshAsync(),
            suggestedOwnedQuery: entry.ResolvedVarName);
    }

    /// <summary>ILoadableScreen: the shell loads this screen by refreshing it. (G-0)</summary>
    Task ILoadableScreen.LoadAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await Pager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        NotifyPager();
    }

    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private async Task PreviousPageAsync(CancellationToken cancellationToken = default)
    {
        await Pager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyPager();
    }

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private async Task NextPageAsync(CancellationToken cancellationToken = default)
    {
        await Pager.NextPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyPager();
    }

    [RelayCommand]
    private async Task GoToPageAsync(int pageNumber)
    {
        await Pager.LoadPageAsync(pageNumber, Pager.PageSize).ConfigureAwait(true);
        NotifyPager();
    }

    [RelayCommand]
    private async Task ChangePageSizeAsync(int pageSize)
    {
        await Pager.LoadPageAsync(1, pageSize).ConfigureAwait(true);
        NotifyPager();
    }

    private bool CanPreviousPage() => Pager.HasPreviousPage && !Pager.IsLoading;
    private bool CanNextPage() => Pager.HasNextPage && !Pager.IsLoading;
    [RelayCommand] private void ToggleShowAll() { }
    private void NotifyPager()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(TotalMissing));
        OnPropertyChanged(nameof(HeaderSummary));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The most recent export text (missing refs, one per line) for save-to-file. Always the FULL set. (AC-17)</summary>
    [ObservableProperty] private string? _lastExportText;

    /// <summary>Screen-head "Export links txt": export ALL the missing refs as a txt list (not just the shown page). (AC-17)</summary>
    [RelayCommand]
    public async Task ExportLinksAsync(CancellationToken cancellationToken = default)
    {
        var all = await query.GetMissingAsync(cancellationToken).ConfigureAwait(true);
        LastExportText = string.Join(System.Environment.NewLine,
            all.OrderByDescending(i => i.NeededByCount).ThenBy(i => i.Ref, StringComparer.Ordinal).Select(i => i.Ref));
    }

    // ── Installed Packages repair (legacy MissingDepends) ───────────────────────────────────────────────
    public ObservableCollection<InstalledDepsEntry> InstalledLeftovers { get; } = [];

    [ObservableProperty] private bool _isAnalyzingInstalled;
    [ObservableProperty] private string? _installedStatus;
    [ObservableProperty] private string? _installedActivationStatus;

    public bool HasInstalledLeftovers => InstalledLeftovers.Count > 0;
    public bool CanAnalyzeInstalled => (installedJobs is not null || installedRepair is not null) && !IsAnalyzingInstalled;

    /// <summary>
    /// Analyse deps of active/installed packages → auto-activate found → list leftovers for alias Resolve.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAnalyzeInstalled))]
    public async Task AnalyzeInstalledAsync()
    {
        IsAnalyzingInstalled = true;
        InstalledStatus = null;
        InstalledActivationStatus = null;
        InstalledLeftovers.Clear();
        NotifyInstalledState();
        try
        {
            Services.InstalledDepsRepairOutcome outcome;
            if (installedJobs is not null)
            {
                InstalledStatus = "Queued — watch the jobs panel…";
                var job = installedJobs.Start();
                outcome = await job.Result.ConfigureAwait(true);
            }
            else if (installedRepair is not null)
            {
                // Headless / unit tests without a job runner.
                var analysis = await installedRepair.AnalyzeAsync().ConfigureAwait(true);
                var activation = await installedRepair.ActivateFromAnalysisAsync(analysis).ConfigureAwait(true);
                outcome = new Services.InstalledDepsRepairOutcome(analysis, activation);
            }
            else
                return;

            ApplyInstalledOutcome(outcome);
            NotifyInstalledState();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            InstalledStatus = "Cancelled.";
        }
        catch (Exception ex)
        {
            InstalledStatus = $"Analyze failed: {ex.Message}";
            InstalledLeftovers.Clear();
        }
        finally
        {
            IsAnalyzingInstalled = false;
            NotifyInstalledState();
        }
    }

    private void ApplyInstalledOutcome(Services.InstalledDepsRepairOutcome outcome)
    {
        var a = outcome.Analysis;
        var act = outcome.Activation;

        if (a.ActivePackageCount == 0)
        {
            InstalledStatus = "No installed packages on the active profile — install something first, then retry.";
            return;
        }

        InstalledStatus = a.Parsed == 0
            ? $"Analyzed {a.ActivePackageCount} installed packages — no dependencies to repair."
            : $"Analyzed {a.ActivePackageCount} installed · {a.Parsed} distinct deps · in library {a.InLibrary} · missing {a.NotInLibrary}" +
              (a.Closest > 0 ? $" · closest substitute {a.Closest}" : "") + ".";

        if (act.PrivilegeFailures > 0)
            InstalledActivationStatus = "Symlink creation needs Developer Mode / admin — members may be queued but links failed.";
        else if (act.PathUnavailable > 0)
            InstalledActivationStatus = "VaM path unset — set Settings, activate a loading preset once, then retry.";
        else if (act.MembersActivated > 0 || act.LinksCreated > 0)
            InstalledActivationStatus =
                $"Activated {act.MembersActivated} packages (+ deps, {act.LinksCreated} links). " +
                $"{act.StillMissing} offline · {act.UnresolvedDependencies} unresolved branches.";
        else if (a.InLibrary > 0)
            InstalledActivationStatus = "Found packages were already on the active preset (or activate was a no-op).";

        foreach (var e in a.Leftovers.OrderByDescending(x => x.NeededByCount).ThenBy(x => x.Ref, StringComparer.Ordinal))
            InstalledLeftovers.Add(e);
    }

    private void NotifyInstalledState()
    {
        OnPropertyChanged(nameof(HasInstalledLeftovers));
        OnPropertyChanged(nameof(CanAnalyzeInstalled));
        AnalyzeInstalledCommand.NotifyCanExecuteChanged();
    }

    // ── VaM-log repair (QoL) ─────────────────────────────────────────────────────────────────────────────
    /// <summary>Raw VaM error-log text pasted by the user (bound to a TextBox).</summary>
    [ObservableProperty] private string _logText = "";

    /// <summary>One row per package the log asked for, resolved against the library.</summary>
    public ObservableCollection<MissingLogEntry> LogEntries { get; } = [];

    [ObservableProperty] private bool _isAnalyzingLog;
    [ObservableProperty] private string? _logStatus;
    [ObservableProperty] private string? _activationStatus;

    public bool HasLogResult => LogEntries.Count > 0;
    public int LogFoundCount => LogEntries.Count(e => e.InLibrary);
    public int LogMissingCount => LogEntries.Count(e => !e.InLibrary);
    public bool CanActivateFound => logResolver is not null && LogFoundCount > 0 && !IsAnalyzingLog;

    /// <summary>Parse the pasted log → resolve each ref (incl. <c>.latest</c>) against the library. (QoL)</summary>
    [RelayCommand]
    public async Task AnalyzeLogAsync()
    {
        if (logResolver is null || string.IsNullOrWhiteSpace(LogText))
            return;
        IsAnalyzingLog = true;
        ActivationStatus = null;
        NotifyLogState();
        try
        {
            var analysis = await logResolver.AnalyzeAsync(LogText).ConfigureAwait(true);
            LogEntries.Clear();
            foreach (var e in analysis.Entries.OrderByDescending(e => e.InLibrary).ThenBy(e => e.Ref, StringComparer.OrdinalIgnoreCase))
                LogEntries.Add(e);
            LogStatus = analysis.Parsed == 0
                ? "No package names found in the log."
                : $"Parsed {analysis.Parsed} packages · in library {analysis.InLibrary} · missing {analysis.NotInLibrary}.";
        }
        catch (Exception ex)
        {
            LogStatus = $"Analyze failed: {ex.Message}";
            LogEntries.Clear();
        }
        finally
        {
            IsAnalyzingLog = false;
            NotifyLogState();
        }
    }

    /// <summary>Add the in-library subset to the active loading preset and rebuild profile links (+ deps). (QoL)</summary>
    [RelayCommand]
    public async Task ActivateFoundAsync()
    {
        if (logResolver is null)
            return;
        var names = LogEntries.Where(e => e.InLibrary && e.ResolvedVarName is not null)
            .Select(e => e.ResolvedVarName!).ToList();
        if (names.Count == 0)
            return;
        IsAnalyzingLog = true;
        NotifyLogState();
        try
        {
            var r = await logResolver.ActivateAsync(names).ConfigureAwait(true);
            ActivationStatus = r.PrivilegeFailures > 0
                ? "Symlink creation needs Developer Mode / admin — nothing activated. Enable Developer Mode and retry."
                : r.PathUnavailable > 0
                    ? "Nothing activated — set VaM path (Settings), activate a loading preset once, then retry."
                    : $"Added {r.MembersActivated} packages to the active loading preset (+ dependencies, {r.LinksCreated} links). " +
                      $"{r.StillMissing} offline/unavailable · {r.UnresolvedDependencies} unresolved dependency branches (need import / Hub).";
        }
        catch (Exception ex)
        {
            ActivationStatus = $"Activate failed: {ex.Message}";
        }
        finally
        {
            IsAnalyzingLog = false;
            NotifyLogState();
        }
    }

    private void NotifyLogState()
    {
        OnPropertyChanged(nameof(HasLogResult));
        OnPropertyChanged(nameof(LogFoundCount));
        OnPropertyChanged(nameof(LogMissingCount));
        OnPropertyChanged(nameof(CanActivateFound));
        AnalyzeLogCommand.NotifyCanExecuteChanged();
        ActivateFoundCommand.NotifyCanExecuteChanged();
    }
}
