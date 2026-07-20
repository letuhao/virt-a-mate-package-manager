using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VarVault.App.ViewModels;

/// <summary>A named action invokable from the command palette.</summary>
public sealed record PaletteCommand(string Name, string Category, Action Invoke);

/// <summary>
/// Command palette (Ctrl-K): a global, type-to-filter list of actions. (Checklist X.14.)
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly IReadOnlyList<PaletteCommand> _all;

    public ObservableCollection<PaletteCommand> Results { get; } = [];

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private bool _isOpen;

    public CommandPaletteViewModel(IEnumerable<PaletteCommand> commands)
    {
        _all = commands?.ToList() ?? throw new ArgumentNullException(nameof(commands));
        Filter();
    }

    public void Open()
    {
        Query = string.Empty;
        IsOpen = true;
    }

    [RelayCommand]
    public void Invoke(PaletteCommand? command)
    {
        command?.Invoke();
        IsOpen = false;
    }

    /// <summary>Esc / backdrop → dismiss without invoking. (24-checklist E9)</summary>
    [RelayCommand]
    public void Close() => IsOpen = false;

    partial void OnQueryChanged(string value) => Filter();

    private void Filter()
    {
        Results.Clear();
        foreach (var command in _all)
        {
            if (string.IsNullOrWhiteSpace(Query) || command.Name.Contains(Query, StringComparison.OrdinalIgnoreCase))
                Results.Add(command);
        }
    }
}
