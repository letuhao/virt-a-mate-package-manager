using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VarVault.App.ViewModels;

/// <summary>
/// An undo toast for reversible actions: shows a message with an Undo action until dismissed.
/// (Checklist X.6.)
/// </summary>
public sealed partial class UndoToastViewModel : ObservableObject
{
    private Func<CancellationToken, Task>? _undo;

    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isVisible;

    /// <summary>Show the toast for a reversible action.</summary>
    public void Show(string message, Func<CancellationToken, Task> undo)
    {
        Message = message;
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));
        IsVisible = true;
    }

    [RelayCommand]
    public async Task UndoAsync(CancellationToken cancellationToken = default)
    {
        if (_undo is not null)
            await _undo(cancellationToken).ConfigureAwait(true);
        IsVisible = false;
        _undo = null;
    }

    [RelayCommand]
    public void Dismiss()
    {
        IsVisible = false;
        _undo = null;
    }
}
