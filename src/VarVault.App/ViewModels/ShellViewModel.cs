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
