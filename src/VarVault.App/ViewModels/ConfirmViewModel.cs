using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VarVault.App.ViewModels;

/// <summary>
/// A confirm-destructive dialog: shows the reverse-dependency impact and hard-blocks single-copy
/// content (it can't be confirmed). (Checklist X.7.)
/// </summary>
public sealed partial class ConfirmViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "Confirm";
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private int _reverseDependentCount;
    [ObservableProperty] private bool _isFoundational;
    [ObservableProperty] private bool _isSingleCopy;
    [ObservableProperty] private bool? _result;

    /// <summary>Single-copy / irreplaceable content can never be confirmed for deletion. (⚠)</summary>
    public bool CanProceed => !IsSingleCopy;

    /// <summary>A human warning about the impact, shown above the buttons.</summary>
    public string ImpactSummary => IsSingleCopy
        ? "This is the only copy — it cannot be deleted."
        : IsFoundational
            ? $"Foundational: {ReverseDependentCount}+ packages depend on this."
            : ReverseDependentCount > 0
                ? $"{ReverseDependentCount} package(s) depend on this."
                : "Nothing depends on this.";

    [RelayCommand(CanExecute = nameof(CanProceed))]
    public void Confirm() => Result = true;

    [RelayCommand]
    public void Cancel() => Result = false;

    partial void OnIsSingleCopyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanProceed));
        OnPropertyChanged(nameof(ImpactSummary));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnReverseDependentCountChanged(int value) => OnPropertyChanged(nameof(ImpactSummary));
    partial void OnIsFoundationalChanged(bool value) => OnPropertyChanged(nameof(ImpactSummary));
}
