using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;

namespace VarVault.App.ViewModels;

/// <summary>Missing-deps screen: unresolved refs with needed-by counts, plus a "paste a VaM error log" repair that
/// resolves the missing packages (incl. <c>.latest</c>) against the library and activates the ones we have — with
/// their dependency closure — into VaM. (Checklist 2.14; 28-checklist D1 triage; QoL log-repair.)</summary>
public sealed partial class MissingDepsViewModel(
    IMissingDepsQuery query, Services.IDialogLauncher? launcher = null, IMissingLogResolver? logResolver = null)
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
            launcher?.OpenAlias(dep.Ref);
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
        finally
        {
            IsAnalyzingLog = false;
            NotifyLogState();
        }
    }

    /// <summary>Activate the in-library subset (+ their dependency closure) into the active VaM profile. (QoL)</summary>
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
                : r.LinksCreated == 0
                    ? "Nothing activated — check the VaM path is set (Settings) and a profile is active."
                    : $"Activated {r.MembersActivated} packages + dependencies ({r.LinksCreated} links). {r.StillMissing} still missing (need import / Hub).";
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
