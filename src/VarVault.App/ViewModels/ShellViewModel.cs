using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VarVault.App.ViewModels;

/// <summary>A rail nav entry: screen id, display label, its rail group, and an icon geometry. (SH-1/SH-2, GC-1.)</summary>
public sealed record ShellScreen(string Id, string Label, string Group, string Icon);

/// <summary>A live rail item — carries active state, an optional badge count, and its icon geometry. (SH-2/GC-1.)</summary>
public sealed partial class RailItemViewModel(string id, string label, string group, string icon) : ObservableObject
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public string Group { get; } = group;
    public string Icon { get; } = icon;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private int? _badge;
}

/// <summary>
/// SH-1 · Shell root: owns the active screen and the navigation command, and holds the ordered rail
/// screen list (grouped Browse/Optimize/Problems/System). Screen view-models are supplied by DI as a
/// map; navigating swaps <see cref="ActiveScreen"/>. (16-checklist SH-1.)
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<string, object> _screens;

    /// <summary>The prototype rail order + grouping (rail nav #1–17). </summary>
    public static readonly IReadOnlyList<ShellScreen> AllScreens =
    [
        new("dashboard", "Dashboard", "Browse", "M3,3 L11,3 L11,11 L3,11 Z M13,3 L21,3 L21,8 L13,8 Z M13,11 L21,11 L21,21 L13,21 Z M3,13 L11,13 L11,21 L3,21 Z"),
        new("library", "Library", "Browse", "M3,3 L21,3 L21,21 L3,21 Z M3,9 L21,9 M9,21 L9,9"),
        new("presets", "Loading presets", "Browse", "M12,3 L21,8 L12,13 L3,8 Z M3,13 L12,18 L21,13"),
        new("tiering", "Tiering & migration", "Optimize", "M3,5 L21,5 M6,12 L18,12 M9,19 L15,19"),
        new("dupes", "Duplicates & reclaim", "Optimize", "M8,8 L20,8 L20,20 L8,20 Z M4,16 L4,6 L6,4 L16,4"),
        new("analytics", "Analytics", "Optimize", "M4,4 L4,20 L20,20 M7,17 L7,12 M12,17 L12,8 M17,17 L17,14"),
        new("proposals", "Proposals", "Problems", "M3,4 L21,4 L21,20 L3,20 Z M8,12 L11,15 L16,9"),
        new("health", "Health & fix", "Problems", "M12,3 L20,6 L20,12 C20,17 16.5,20 12,21 C7.5,20 4,17 4,12 L4,6 Z"),
        new("missing", "Missing deps", "Problems", "M12,3 L21,19 L3,19 Z M12,9 L12,13"),
        new("repos", "Repositories", "System", "M4,5 C4,3 20,3 20,5 L20,19 C20,21 4,21 4,19 Z M4,12 C4,14 20,14 20,12"),
        new("trash", "Trash & backup", "System", "M3,6 L21,6 M8,6 L8,4 L16,4 L16,6 M6,6 L7,20 L17,20 L18,6"),
        new("history", "Activity history", "System", "M4,12 A8,8 0 1 0 8,5 M4,5 L4,9 L8,9 M12,8 L12,12 L15,14"),
        new("settings", "Settings", "System", "M12,9 A3,3 0 1 0 12.01,9 M12,2 L12,5 M12,19 L12,22 M4,4 L6,6 M18,18 L20,20"),
    ];

    private readonly Sdk.Threading.IJobQueue? _jobQueue;

    public ShellViewModel(IReadOnlyDictionary<string, object> screens, string initial = "library",
        Services.IDialogService? dialogs = null, Sdk.Threading.IJobQueue? jobQueue = null,
        Services.IShellLiveFeeds? feeds = null)
    {
        _screens = screens;
        Dialogs = dialogs ?? new Services.DialogService();
        _jobQueue = jobQueue;
        _feeds = feeds;
        Screens = AllScreens;
        RailItems = AllScreens.Select(s => new RailItemViewModel(s.Id, s.Label, s.Group, s.Icon)).ToList();
        Navigate(initial);
    }

    private readonly Services.IShellLiveFeeds? _feeds;

    /// <summary>
    /// Pull all live state (jobs, badges, log-dock) from the injected sources. Called by the shell's poll
    /// timer and after actions. (GA-2/GA-3/GA-4.)
    /// </summary>
    public async System.Threading.Tasks.Task RefreshLiveStateAsync(
        System.Threading.CancellationToken cancellationToken = default)
    {
        RefreshJobsFromQueue();
        if (_feeds is null)
            return;
        var snap = await _feeds.SnapshotAsync(cancellationToken).ConfigureAwait(true);
        SetBadge("proposals", snap.ProposalCount == 0 ? null : snap.ProposalCount);
        SetBadge("health", snap.HealthCount == 0 ? null : snap.HealthCount);
        SetBadge("missing", snap.MissingCount == 0 ? null : snap.MissingCount);
        TierSummary = snap.TierSummary;
        IndexStatus = snap.IndexStatus;
        PackageCountLabel = snap.TotalPackages.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " pkgs";
    }

    /// <summary>Refresh the jobs panel from the live <see cref="Sdk.Threading.IJobQueue"/>. (GA-2)</summary>
    public void RefreshJobsFromQueue()
    {
        if (_jobQueue is not null)
            RefreshJobs(_jobQueue.Active);
    }

    /// <summary>The shell's single dialog host (bound by the ModalHost in MainWindow). (GA-1)</summary>
    public Services.IDialogService Dialogs { get; }

    public IReadOnlyList<ShellScreen> Screens { get; }

    /// <summary>Live rail items (grouped in the view) with active state + badges. (SH-2)</summary>
    public IReadOnlyList<RailItemViewModel> RailItems { get; }

    public IEnumerable<RailItemViewModel> BrowseItems => RailItems.Where(r => r.Group == "Browse");
    public IEnumerable<RailItemViewModel> OptimizeItems => RailItems.Where(r => r.Group == "Optimize");
    public IEnumerable<RailItemViewModel> ProblemItems => RailItems.Where(r => r.Group == "Problems");
    public IEnumerable<RailItemViewModel> SystemItems => RailItems.Where(r => r.Group == "System");

    /// <summary>Set a rail badge count by screen id (proposals/health/missing). (SH-2)</summary>
    public void SetBadge(string screenId, int? count)
    {
        foreach (var item in RailItems)
            if (item.Id == screenId)
                item.Badge = count;
    }

    [ObservableProperty] private object? _activeScreen;
    [ObservableProperty] private string _activeScreenId = "";

    // Top bar (SH-3)
    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private bool _jobsPanelOpen;
    [ObservableProperty] private bool _hasActiveJobs;
    [ObservableProperty] private bool _paletteOpen;

    // Rail branding (GC-1)
    [ObservableProperty] private string _packageCountLabel = "0 pkgs";

    // Log dock (SH-5)
    [ObservableProperty] private string? _indexStatus;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string? _tierSummary;

    // Toast / undo (DLG-11)
    [ObservableProperty] private string? _toastMessage;
    [ObservableProperty] private bool _toastVisible;
    [ObservableProperty] private bool _toastHasUndo;
    private System.Action? _toastUndo;

    /// <summary>Show a transient toast; the optional undo reverses the just-completed action. (DLG-11)</summary>
    public void ShowToast(string message, System.Action? undo = null)
    {
        ToastMessage = message;
        _toastUndo = undo;
        ToastHasUndo = undo is not null;
        ToastVisible = true;
    }

    [RelayCommand]
    private void UndoToast()
    {
        _toastUndo?.Invoke();
        ToastVisible = false;
    }

    [RelayCommand]
    private void DismissToast() => ToastVisible = false;

    /// <summary>Hook the shell can set to run the rescue baseline (wired in AppHost, SH-6).</summary>
    public System.Func<System.Threading.Tasks.Task>? RescueHandler { get; set; }
    /// <summary>Hook to open the add-repository dialog (wired in AppHost, SH-6).</summary>
    public System.Action? AddRepoHandler { get; set; }

    /// <summary>Live background jobs shown in the panel (refreshed from IJobQueue by the shell). (SH-4)</summary>
    public System.Collections.ObjectModel.ObservableCollection<Sdk.Threading.JobHandle> ActiveJobs { get; } = new();

    /// <summary>Replace the active-jobs snapshot (AppHost polls IJobQueue and calls this). (SH-4)</summary>
    public void RefreshJobs(System.Collections.Generic.IReadOnlyList<Sdk.Threading.JobHandle> jobs)
    {
        ActiveJobs.Clear();
        foreach (var j in jobs)
            ActiveJobs.Add(j);
        HasActiveJobs = jobs.Count > 0;
    }

    [RelayCommand]
    private void PauseAll()
    {
        foreach (var j in ActiveJobs)
            j.Cancel();
    }

    [RelayCommand] private void ToggleJobs() => JobsPanelOpen = !JobsPanelOpen;
    [RelayCommand] private void AddRepo() => AddRepoHandler?.Invoke();

    /// <summary>Handler set by AppHost: run the top-bar search (drives the library filter). (AC-8)</summary>
    public System.Action<string>? SearchHandler { get; set; }

    /// <summary>Top-bar search Enter: run the global search through the wired handler; falls back to the flag. (AC-8)</summary>
    [RelayCommand]
    private void OpenPalette()
    {
        if (SearchHandler is not null)
            SearchHandler(SearchText ?? string.Empty);
        else
            PaletteOpen = true;
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task RescueAsync()
    {
        if (RescueHandler is not null)
            await RescueHandler().ConfigureAwait(true);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        if (Avalonia.Application.Current is { } app)
            app.RequestedThemeVariant = app.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
                ? Avalonia.Styling.ThemeVariant.Light
                : Avalonia.Styling.ThemeVariant.Dark;
    }

    /// <summary>Navigate to a screen by id; swaps the active screen view-model if one is registered.</summary>
    [RelayCommand]
    public void Navigate(string screenId)
    {
        if (string.IsNullOrEmpty(screenId))
            return;
        ActiveScreenId = screenId;
        ActiveScreen = _screens.TryGetValue(screenId, out var vm) ? vm : null;
        foreach (var item in RailItems)
            item.IsActive = item.Id == screenId;
    }
}
