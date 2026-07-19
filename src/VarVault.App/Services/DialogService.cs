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

    /// <summary>Show <paramref name="dialogViewModel"/> as the active modal.</summary>
    void Show(object dialogViewModel);

    /// <summary>Dismiss the active modal.</summary>
    void Close();
}

/// <summary>Observable implementation so the ModalHost binds to <see cref="Current"/>/<see cref="IsOpen"/>.</summary>
public sealed partial class DialogService : ObservableObject, IDialogService
{
    [ObservableProperty] private object? _current;
    [ObservableProperty] private bool _isOpen;

    public void Show(object dialogViewModel)
    {
        Current = dialogViewModel;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        Current = null;
    }
}
