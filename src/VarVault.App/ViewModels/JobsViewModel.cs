using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Threading;

namespace VarVault.App.ViewModels;

/// <summary>
/// The jobs tray: the live background jobs with progress + cancel. Binds to <see cref="IJobQueue"/>.
/// (Checklist 1.53, X.11.)
/// </summary>
public sealed partial class JobsViewModel(IJobQueue queue) : ObservableObject
{
    public ObservableCollection<JobHandle> Jobs { get; } = [];

    public bool HasJobs => Jobs.Count > 0;

    /// <summary>Sync the tray from the queue's active jobs (called on a UI tick / job events).</summary>
    [RelayCommand]
    public void Refresh()
    {
        Jobs.Clear();
        foreach (var job in queue.Active)
            Jobs.Add(job);
        OnPropertyChanged(nameof(HasJobs));
    }

    [RelayCommand]
    public void Cancel(JobHandle? job) => job?.Cancel();
}
