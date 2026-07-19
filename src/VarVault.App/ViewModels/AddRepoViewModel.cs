using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Repositories;

namespace VarVault.App.ViewModels;

/// <summary>DLG-2 · Add repository: register a folder, show detected media/tier. (16-checklist DLG-2.)</summary>
public sealed partial class AddRepoViewModel(IRepositoryService repositories, Action? onAdded = null) : ObservableObject
{
    [ObservableProperty] private string? _folderPath;
    [ObservableProperty] private string? _reserve = "200 GB";
    [ObservableProperty] private RepositoryInfo? _registered;
    [ObservableProperty] private string? _message;

    /// <summary>Whether to also rebalance existing data onto this drive (prototype checkbox). (GE-2)</summary>
    [ObservableProperty] private bool _rebalanceExisting;

    [RelayCommand]
    public async Task AddAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(FolderPath))
            return;
        var r = await repositories.RegisterAsync(new RegisterRepositoryRequest("repository", FolderPath!), cancellationToken).ConfigureAwait(true);
        if (r.IsSuccess)
        {
            Registered = r.Value;
            Message = $"Detected {r.Value.MediaType} → Tier {r.Value.Tier}";
            onAdded?.Invoke(); // GA-5/GA-6: kick off indexing of the new repo
        }
        else
        {
            Message = r.Error.Message;
        }
    }
}
