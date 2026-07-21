using CommunityToolkit.Mvvm.ComponentModel;

namespace VarVault.App.Services;

/// <summary>
/// GA-1 · Opens/closes modal dialogs. Screens raise "show this dialog view-model"; the single
/// <c>ModalHost</c> in the shell binds to <see cref="Current"/> + <see cref="IsOpen"/>. No screen news-up a
/// window. (18-gap GA-1.)
/// </summary>
public interface IDialogService
{
    /// <summary>The dialog view-model currently hosted (null when closed).</summary>
    object? Current { get; }

    /// <summary>Whether a dialog is open (drives the ModalHost backdrop).</summary>
    bool IsOpen { get; }

    /// <summary>True when a nested dialog can pop back to the previous one.</summary>
    bool CanGoBack { get; }

    /// <summary>Show <paramref name="dialogViewModel"/> as the active modal (clears any stack).</summary>
    void Show(object dialogViewModel);

    /// <summary>Push a child dialog over the current one; the previous VM is restored by <see cref="Back"/>.</summary>
    void Push(object dialogViewModel);

    /// <summary>Pop to the previous dialog, or close when the stack is empty.</summary>
    void Back();

    /// <summary>Dismiss the active modal and clear the stack.</summary>
    void Close();
}

/// <summary>Observable implementation so the ModalHost binds to <see cref="Current"/>/<see cref="IsOpen"/>.</summary>
public sealed partial class DialogService : ObservableObject, IDialogService
{
    private readonly Stack<object> _stack = new();

    [ObservableProperty] private object? _current;
    [ObservableProperty] private bool _isOpen;

    public bool CanGoBack => _stack.Count > 0;

    public void Show(object dialogViewModel)
    {
        _stack.Clear();
        Current = dialogViewModel;
        IsOpen = true;
        OnPropertyChanged(nameof(CanGoBack));
    }

    public void Push(object dialogViewModel)
    {
        if (Current is not null)
            _stack.Push(Current);
        Current = dialogViewModel;
        IsOpen = true;
        OnPropertyChanged(nameof(CanGoBack));
    }

    public void Back()
    {
        if (_stack.Count == 0)
        {
            Close();
            return;
        }

        Current = _stack.Pop();
        IsOpen = true;
        OnPropertyChanged(nameof(CanGoBack));
    }

    public void Close()
    {
        _stack.Clear();
        IsOpen = false;
        Current = null;
        OnPropertyChanged(nameof(CanGoBack));
    }
}
