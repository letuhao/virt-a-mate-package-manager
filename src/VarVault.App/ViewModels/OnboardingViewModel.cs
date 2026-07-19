using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>The onboarding steps, in order. (X.8)</summary>
public enum OnboardingStep { AddDrives, Benchmark, Index, Rescue, Done }

/// <summary>
/// DLG-1 · Onboarding wizard: add drives → benchmark → index → rescue. A linear step machine the view
/// renders one page at a time; add-and-index goes through <see cref="IOnboardingService"/> (BE-N13).
/// (Checklist X.8 / 16-checklist DLG-1.)
/// </summary>
public sealed partial class OnboardingViewModel(IOnboardingService? onboarding = null) : ObservableObject
{
    [ObservableProperty] private OnboardingStep _step = OnboardingStep.AddDrives;
    [ObservableProperty] private string? _folderPath;
    [ObservableProperty] private string? _resultMessage;

    /// <summary>Folders that have been added + benchmarked/indexed (the benchmarked-folder table). (AC-29)</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> BenchmarkedFolders { get; } = [];

    [RelayCommand]
    public async Task AddAndIndexAsync(CancellationToken cancellationToken = default)
    {
        if (onboarding is null || string.IsNullOrWhiteSpace(FolderPath))
            return;
        var result = await onboarding.AddAndIndexAsync("repository", FolderPath!, cancellationToken: cancellationToken).ConfigureAwait(true);
        if (result.IsSuccess)
        {
            ResultMessage = $"Indexed {result.Value.Index.Indexed} vars";
            BenchmarkedFolders.Add($"{FolderPath} — indexed {result.Value.Index.Indexed} vars");
        }
        else
        {
            ResultMessage = result.Error.Message;
        }
        Step = OnboardingStep.Done;
    }

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
