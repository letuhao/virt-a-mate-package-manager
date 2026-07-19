using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VarVault.App.ViewModels;

/// <summary>A rail nav entry: screen id, display label, and its rail group. (16-checklist SH-1/SH-2.)</summary>
public sealed record ShellScreen(string Id, string Label, string Group);

/// <summary>A live rail item — carries active state and an optional badge count. (16-checklist SH-2.)</summary>
public sealed partial class RailItemViewModel(string id, string label, string group) : ObservableObject
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public string Group { get; } = group;
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
        new("dashboard", "Dashboard", "Browse"),
        new("library", "Library", "Browse"),
        new("presets", "Loading presets", "Browse"),
        new("tiering", "Tiering & migration", "Optimize"),
        new("dupes", "Duplicates & reclaim", "Optimize"),
        new("analytics", "Analytics", "Optimize"),
        new("proposals", "Proposals", "Problems"),
        new("health", "Health & fix", "Problems"),
        new("missing", "Missing deps", "Problems"),
        new("repos", "Repositories", "System"),
        new("trash", "Trash & backup", "System"),
        new("history", "Activity history", "System"),
        new("settings", "Settings", "System"),
    ];

    public ShellViewModel(IReadOnlyDictionary<string, object> screens, string initial = "library")
    {
        _screens = screens;
        Screens = AllScreens;
        RailItems = AllScreens.Select(s => new RailItemViewModel(s.Id, s.Label, s.Group)).ToList();
        Navigate(initial);
    }

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

    // Log dock (SH-5)
    [ObservableProperty] private string? _indexStatus;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string? _tierSummary;

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
    [RelayCommand] private void OpenPalette() => PaletteOpen = true;
    [RelayCommand] private void AddRepo() => AddRepoHandler?.Invoke();

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
