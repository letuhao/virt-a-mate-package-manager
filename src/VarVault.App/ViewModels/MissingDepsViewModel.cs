using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.App.Services;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;

namespace VarVault.App.ViewModels;

/// <summary>Missing-deps screen: unresolved refs with needed-by counts, VaM-log repair, and installed-set
/// dependency repair (legacy Installed Packages / MissingDepends). (Checklist 2.14; QoL.)</summary>
public sealed partial class MissingDepsViewModel(
    IMissingDepsQuery query,
    IDialogLauncher? launcher = null,
    IMissingLogResolver? logResolver = null,
    IInstalledDepsRepair? installedRepair = null,
    InstalledDepsRepairJobRunner? installedJobs = null,
    IClipboard? clipboard = null)
    : ObservableObject, ILoadableScreen
{
    private readonly List<InstalledDepsEntry> _allInstalledLeftovers = [];

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
          + "VarVault can't create these; use “Export links txt” to fetch them, or “Resolve” / “Manage aliases” to map a ref to a package you do have.";

    /// <summary>Resolve/Edit-alias → manage-aliases dialog focused on this missing ref.</summary>
    [RelayCommand]
    private void Resolve(MissingDependency dep)
    {
        if (dep is not null)
            launcher?.OpenManageAliases(onChanged: () => _ = OnAliasChangedAsync(dep.Ref), focusMissingRef: dep.Ref);
    }

    /// <summary>Resolve a leftover from installed-deps analyze (optional suggested owned var).</summary>
    [RelayCommand]
    private void ResolveInstalledLeftover(InstalledDepsEntry? entry)
    {
        if (entry is null)
            return;
        launcher?.OpenManageAliases(
            onChanged: () => _ = OnAliasChangedAsync(entry.Ref),
            focusMissingRef: entry.Ref,
            suggestedOwnedQuery: entry.ResolvedVarName);
    }

    /// <summary>Open the FormMissingVars-style alias review (list / unlink / add).</summary>
    [RelayCommand]
    private void ManageAliases() =>
        launcher?.OpenManageAliases(onChanged: () => _ = OnAliasChangedAsync(null));

    /// <summary>ILoadableScreen: the shell loads this screen by refreshing it. (G-0)</summary>
    Task ILoadableScreen.LoadAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await Pager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        NotifyPager();
    }

    /// <summary>After alias set/remove: drop leftover row immediately and reload the missing page.</summary>
    public async Task OnAliasChangedAsync(string? missingRef, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(missingRef))
            RemoveInstalledLeftover(missingRef);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CopyRefAsync(string? text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || clipboard is null)
            return;
        await clipboard.SetTextAsync(text.Trim(), cancellationToken).ConfigureAwait(true);
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

    /// <summary>Save-file picker hook (set by the view).</summary>
    public Func<string, Task<string?>>? SaveTxtPicker { get; set; }

    /// <summary>Status from the last Export links txt action.</summary>
    [ObservableProperty] private string? _exportStatus;

    /// <summary>The most recent export text (missing refs, one per line) for save-to-file. Always the FULL set. (AC-17)</summary>
    [ObservableProperty] private string? _lastExportText;

    /// <summary>Screen-head "Export links txt": export ALL the missing refs as a txt file. (AC-17)</summary>
    [RelayCommand]
    public async Task ExportLinksAsync(CancellationToken cancellationToken = default)
    {
        var all = await query.GetMissingAsync(cancellationToken).ConfigureAwait(true);
        var text = string.Join(System.Environment.NewLine,
            all.OrderByDescending(i => i.NeededByCount).ThenBy(i => i.Ref, StringComparer.Ordinal).Select(i => i.Ref));
        ExportStatus = await Services.TxtFileIo.ExportAsync(
            SaveTxtPicker, "missing-links.txt", text, t => LastExportText = t, cancellationToken).ConfigureAwait(true);
    }

    // ── Installed Packages repair (legacy MissingDepends) ───────────────────────────────────────────────
    public ObservableCollection<InstalledDepsEntry> InstalledLeftovers { get; } = [];

    [ObservableProperty] private bool _isAnalyzingInstalled;
    [ObservableProperty] private string? _installedStatus;
    [ObservableProperty] private string? _installedActivationStatus;
    [ObservableProperty] private int _leftoverPageNumber = 1;
    [ObservableProperty] private int _leftoverPageSize = 50;

    public bool HasInstalledLeftovers => _allInstalledLeftovers.Count > 0;
    public bool CanAnalyzeInstalled => (installedJobs is not null || installedRepair is not null) && !IsAnalyzingInstalled;
    public int LeftoverTotalCount => _allInstalledLeftovers.Count;
    public int LeftoverPageCount => Math.Max(1, (int)Math.Ceiling(LeftoverTotalCount / (double)Math.Max(1, LeftoverPageSize)));
    public string LeftoverSummaryLabel => LeftoverTotalCount == 0
        ? "0 leftovers"
        : $"{LeftoverTotalCount} leftover{(LeftoverTotalCount == 1 ? "" : "s")} · page {LeftoverPageNumber}/{LeftoverPageCount}";
    public bool CanLeftoverPrevious => LeftoverPageNumber > 1;
    public bool CanLeftoverNext => LeftoverPageNumber < LeftoverPageCount;

    /// <summary>
    /// Analyse deps of active/installed packages → auto-activate found → list leftovers for alias Resolve.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAnalyzeInstalled))]
    public async Task AnalyzeInstalledAsync()
    {
        IsAnalyzingInstalled = true;
        InstalledStatus = null;
        InstalledActivationStatus = null;
        _allInstalledLeftovers.Clear();
        RebuildLeftoverPage();
        NotifyInstalledState();
        try
        {
            InstalledDepsRepairOutcome outcome;
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
                outcome = new InstalledDepsRepairOutcome(analysis, activation);
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
            _allInstalledLeftovers.Clear();
            RebuildLeftoverPage();
        }
        finally
        {
            IsAnalyzingInstalled = false;
            NotifyInstalledState();
        }
    }

    private void ApplyInstalledOutcome(InstalledDepsRepairOutcome outcome)
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

        _allInstalledLeftovers.Clear();
        foreach (var e in a.Leftovers.OrderByDescending(x => x.NeededByCount).ThenBy(x => x.Ref, StringComparer.Ordinal))
            _allInstalledLeftovers.Add(e);
        LeftoverPageNumber = 1;
        RebuildLeftoverPage();
    }

    private void RemoveInstalledLeftover(string missingRef)
    {
        _allInstalledLeftovers.RemoveAll(e =>
            string.Equals(e.Ref, missingRef, StringComparison.OrdinalIgnoreCase));
        if (LeftoverPageNumber > LeftoverPageCount)
            LeftoverPageNumber = LeftoverPageCount;
        RebuildLeftoverPage();
        NotifyInstalledState();
    }

    private void RebuildLeftoverPage()
    {
        InstalledLeftovers.Clear();
        var size = Math.Max(1, LeftoverPageSize);
        var page = Math.Clamp(LeftoverPageNumber, 1, LeftoverPageCount);
        LeftoverPageNumber = page;
        foreach (var e in _allInstalledLeftovers.Skip((page - 1) * size).Take(size))
            InstalledLeftovers.Add(e);
        OnPropertyChanged(nameof(LeftoverTotalCount));
        OnPropertyChanged(nameof(LeftoverPageCount));
        OnPropertyChanged(nameof(LeftoverSummaryLabel));
        OnPropertyChanged(nameof(CanLeftoverPrevious));
        OnPropertyChanged(nameof(CanLeftoverNext));
        LeftoverPreviousCommand.NotifyCanExecuteChanged();
        LeftoverNextCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanLeftoverPrevious))]
    private void LeftoverPrevious()
    {
        if (LeftoverPageNumber <= 1)
            return;
        LeftoverPageNumber--;
        RebuildLeftoverPage();
    }

    [RelayCommand(CanExecute = nameof(CanLeftoverNext))]
    private void LeftoverNext()
    {
        if (LeftoverPageNumber >= LeftoverPageCount)
            return;
        LeftoverPageNumber++;
        RebuildLeftoverPage();
    }

    [RelayCommand]
    private void LeftoverGoToPage(int pageNumber)
    {
        LeftoverPageNumber = pageNumber;
        RebuildLeftoverPage();
    }

    [RelayCommand]
    private void LeftoverChangePageSize(int pageSize)
    {
        LeftoverPageSize = Math.Max(1, pageSize);
        LeftoverPageNumber = 1;
        RebuildLeftoverPage();
    }

    private void NotifyInstalledState()
    {
        OnPropertyChanged(nameof(HasInstalledLeftovers));
        OnPropertyChanged(nameof(CanAnalyzeInstalled));
        OnPropertyChanged(nameof(LeftoverTotalCount));
        OnPropertyChanged(nameof(LeftoverPageCount));
        OnPropertyChanged(nameof(LeftoverSummaryLabel));
        OnPropertyChanged(nameof(CanLeftoverPrevious));
        OnPropertyChanged(nameof(CanLeftoverNext));
        AnalyzeInstalledCommand.NotifyCanExecuteChanged();
        LeftoverPreviousCommand.NotifyCanExecuteChanged();
        LeftoverNextCommand.NotifyCanExecuteChanged();
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
                      $"{r.StillMissing} offline/unavailable · {r.UnresolvedDependencies} unresolved dependency branches (need import).";
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
