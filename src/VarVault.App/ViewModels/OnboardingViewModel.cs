using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VarVault.App.ViewModels;

/// <summary>The onboarding steps, in order. (X.8)</summary>
public enum OnboardingStep { AddDrives, Benchmark, Index, Rescue, Done }

/// <summary>
/// Onboarding wizard: add drives → benchmark → index → rescue. A linear step machine the view renders
/// one page at a time. (Checklist X.8.)
/// </summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    [ObservableProperty] private OnboardingStep _step = OnboardingStep.AddDrives;

    public bool CanAdvance => Step < OnboardingStep.Done;
    public bool CanGoBack => Step > OnboardingStep.AddDrives && Step < OnboardingStep.Done;
    public bool IsComplete => Step == OnboardingStep.Done;

    [RelayCommand(CanExecute = nameof(CanAdvance))]
    public void Next()
    {
        if (Step < OnboardingStep.Done)
            Step++;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    public void Back()
    {
        if (Step > OnboardingStep.AddDrives)
            Step--;
    }

    partial void OnStepChanged(OnboardingStep value)
    {
        OnPropertyChanged(nameof(CanAdvance));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsComplete));
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }
}
