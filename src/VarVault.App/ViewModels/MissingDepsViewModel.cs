using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>Missing-deps screen: unresolved refs with needed-by counts. (Checklist 2.14; 28-checklist D1 triage.)</summary>
public sealed partial class MissingDepsViewModel(
    IMissingDepsQuery query, Services.IDialogLauncher? launcher = null) : ObservableObject, ILoadableScreen
{
    /// <summary>Initial render cap — a large library can reference thousands of missing packages; showing them all
    /// up-front is slow and overwhelming. Most-needed-first + a "show all" toggle keeps triage fast. (D1.2)</summary>
    private const int Cap = 200;

    private readonly List<MissingDependency> _all = [];

    /// <summary>Rows currently rendered (capped subset unless <see cref="ShowAll"/>). </summary>
    public ObservableCollection<MissingDependency> Items { get; } = [];

    public bool IsEmpty => _all.Count == 0;

    /// <summary>Total distinct referenced-but-absent packages (the full set, not the capped view). (D1.1)</summary>
    public int TotalMissing => _all.Count;

    [ObservableProperty] private bool _showAll;

    /// <summary>True while the view is capped (more exist than are shown). (D1.2)</summary>
    public bool IsCapped => !ShowAll && _all.Count > Cap;

    /// <summary>Whether the "show all / show top" toggle is worth showing at all.</summary>
    public bool CanToggle => _all.Count > Cap;

    /// <summary>One-line framing so the number has context and the newcomer knows these are downloads, not a bug. (D1.1/D1.3)</summary>
    public string HeaderSummary => _all.Count == 0
        ? "No missing dependencies — every referenced package is in your library."
        : $"{_all.Count} packages are referenced by your library but aren't in it — downloads you still need. "
          + "VarVault can't create these; use “Export links txt” to fetch them, or “Resolve” to map a ref to a package you do have.";

    /// <summary>Cap status line under the header. (D1.2)</summary>
    public string CapLabel => _all.Count == 0
        ? string.Empty
        : IsCapped ? $"Showing the {Cap} most-needed of {_all.Count}." : $"Showing all {_all.Count}, most-needed first.";

    /// <summary>Toggle button label. (D1.2)</summary>
    public string ToggleLabel => ShowAll ? $"Show top {Cap}" : $"Show all {_all.Count}";

    /// <summary>Resolve/Edit-alias → alias dialog for the missing ref. (GD-12)</summary>
    [RelayCommand]
    private void Resolve(MissingDependency dep)
    {
        if (dep is not null)
            launcher?.OpenAlias(dep.Ref);
    }

    /// <summary>Show all rows / collapse back to the top-N. (D1.2)</summary>
    [RelayCommand]
    private void ToggleShowAll() => ShowAll = !ShowAll;

    partial void OnShowAllChanged(bool value) => Apply();

    /// <summary>ILoadableScreen: the shell loads this screen by refreshing it. (G-0)</summary>
    Task ILoadableScreen.LoadAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _all.Clear();
        foreach (var item in await query.GetMissingAsync(cancellationToken).ConfigureAwait(true))
            _all.Add(item);
        Apply();
    }

    private void Apply()
    {
        Items.Clear();
        // Most-needed first: the packages blocking the most of your library are the highest-value downloads. (D1.2)
        IEnumerable<MissingDependency> ordered = _all
            .OrderByDescending(i => i.NeededByCount)
            .ThenBy(i => i.Ref, StringComparer.OrdinalIgnoreCase);
        foreach (var item in ShowAll ? ordered : ordered.Take(Cap))
            Items.Add(item);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(TotalMissing));
        OnPropertyChanged(nameof(IsCapped));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(HeaderSummary));
        OnPropertyChanged(nameof(CapLabel));
        OnPropertyChanged(nameof(ToggleLabel));
    }

    /// <summary>The most recent export text (missing refs, one per line) for save-to-file. Always the FULL set. (AC-17)</summary>
    [ObservableProperty] private string? _lastExportText;

    /// <summary>Screen-head "Export links txt": export ALL the missing refs as a txt list (not just the shown page). (AC-17)</summary>
    [RelayCommand]
    public void ExportLinks() =>
        LastExportText = string.Join(System.Environment.NewLine,
            _all.OrderByDescending(i => i.NeededByCount).Select(i => i.Ref));
}
