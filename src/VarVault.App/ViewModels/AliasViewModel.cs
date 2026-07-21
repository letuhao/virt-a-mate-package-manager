using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>DLG-5 · Resolve missing dependency: search the owned library and map a missing ref to a chosen
/// owned package. The search input drives <see cref="OwnedPackageId"/> so Save persists a real target.
/// (16-checklist DLG-5 / AC-6.)</summary>
public sealed partial class AliasViewModel(IAliasService aliases, ILibraryQueryService? library = null, Action? close = null)
    : ObservableObject
{
    /// <summary>Invoked after a successful save (e.g. reload parent detail).</summary>
    public Action? OnSaved { get; init; }
    [ObservableProperty] private string? _missingRef;
    [ObservableProperty] private long _ownedPackageId;
    [ObservableProperty] private string? _statusMessage;

    /// <summary>Owned-package search text (typed into the "map to owned" box). (AC-6)</summary>
    [ObservableProperty] private string? _ownedQuery;

    /// <summary>The owned package chosen from the search results — sets <see cref="OwnedPackageId"/>. (AC-6)</summary>
    [ObservableProperty] private PackageListEntry? _selectedOwned;

    /// <summary>Owned packages matching <see cref="OwnedQuery"/> (the searchable-combo item source). (AC-6)</summary>
    public ObservableCollection<PackageListEntry> OwnedMatches { get; } = [];

    /// <summary>True once a real owned package is chosen — gates Save so it never persists id 0. (AC-6)</summary>
    public bool CanSave => OwnedPackageId != 0 && !string.IsNullOrWhiteSpace(MissingRef);

    partial void OnOwnedQueryChanged(string? value) => SearchTask = SearchOwnedAsync(value);

    /// <summary>Exposed so tests can await the search settle.</summary>
    public Task? SearchTask { get; private set; }

    private async Task SearchOwnedAsync(string? text)
    {
        if (library is null || string.IsNullOrWhiteSpace(text))
        {
            OwnedMatches.Clear();
            return;
        }
        var page = await library.GetPageAsync(new LibraryQuery(Skip: 0, Take: 20, SearchText: text)).ConfigureAwait(true);
        OwnedMatches.Clear();
        foreach (var item in page.Items)
            OwnedMatches.Add(item);
    }

    partial void OnSelectedOwnedChanged(PackageListEntry? value)
    {
        OwnedPackageId = value?.PackageId ?? 0;
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnMissingRefChanged(string? value)
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(MissingRef) || OwnedPackageId == 0)
        {
            StatusMessage = "Choose an owned package first.";
            return;
        }
        var result = await aliases.SetAsync(MissingRef!, OwnedPackageId, cancellationToken).ConfigureAwait(true);
        StatusMessage = result.IsSuccess ? "Alias saved" : result.Error.Message;
        if (result.IsSuccess)
        {
            OnSaved?.Invoke();
            close?.Invoke();
        }
    }
}
