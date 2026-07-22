using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.App.Services;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>DLG-5 · Resolve missing dependency: search the owned library and map a missing ref to a chosen
/// owned package. The search input drives <see cref="OwnedPackageId"/> so Save persists a real target.
/// (16-checklist DLG-5 / AC-6.)</summary>
public sealed partial class AliasViewModel(
    IAliasService aliases,
    ILibraryQueryService? library = null,
    Action? close = null,
    IClipboard? clipboard = null)
    : ObservableObject
{
    private int _searchEpoch;
    private bool _suppressSearch;
    private bool _suppressSelection;

    /// <summary>Invoked after a successful save (e.g. reload parent detail).</summary>
    public Action? OnSaved { get; init; }
    [ObservableProperty] private string? _missingRef;
    [ObservableProperty] private long _ownedPackageId;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _clipboardStatus;

    /// <summary>Owned-package search text (typed into the "map to owned" box). (AC-6)</summary>
    [ObservableProperty] private string? _ownedQuery;

    /// <summary>The owned package chosen from the search results — sets <see cref="OwnedPackageId"/>. (AC-6)</summary>
    [ObservableProperty] private PackageListEntry? _selectedOwned;

    /// <summary>Owned packages matching <see cref="OwnedQuery"/> (list under the search box). (AC-6)</summary>
    public ObservableCollection<PackageListEntry> OwnedMatches { get; } = [];

    /// <summary>True once a real owned package is chosen — gates Save so it never persists id 0. (AC-6)</summary>
    public bool CanSave => OwnedPackageId != 0 && !string.IsNullOrWhiteSpace(MissingRef);

    partial void OnOwnedQueryChanged(string? value)
    {
        if (_suppressSearch)
            return;
        SearchTask = SearchOwnedAsync(value);
    }

    /// <summary>Exposed so tests can await the search settle.</summary>
    public Task? SearchTask { get; private set; }

    private async Task SearchOwnedAsync(string? text)
    {
        var epoch = ++_searchEpoch;
        if (library is null || string.IsNullOrWhiteSpace(text))
        {
            if (epoch == _searchEpoch)
                ReplaceMatches([]);
            return;
        }

        try
        {
            var page = await library.GetPageAsync(new LibraryQuery(Skip: 0, Take: 40, SearchText: text)).ConfigureAwait(true);
            if (epoch != _searchEpoch)
                return;
            ReplaceMatches(page.Items);
        }
        catch (Exception ex)
        {
            if (epoch == _searchEpoch)
                StatusMessage = $"Search failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Replace match rows without letting ListBox null-out <see cref="SelectedOwned"/> /
    /// <see cref="OwnedPackageId"/> mid-update (that path crashed Avalonia AutoCompleteBox and can still
    /// wipe a valid pick when the ItemsSource is rebound).
    /// </summary>
    private void ReplaceMatches(IReadOnlyList<PackageListEntry> items)
    {
        var keepId = SelectedOwned?.PackageId ?? OwnedPackageId;
        _suppressSelection = true;
        try
        {
            OwnedMatches.Clear();
            foreach (var item in items)
                OwnedMatches.Add(item);
        }
        finally
        {
            _suppressSelection = false;
        }

        if (keepId > 0)
        {
            var kept = OwnedMatches.FirstOrDefault(m => m.PackageId == keepId);
            if (kept is not null)
                SelectedOwned = kept;
            else
            {
                // Selection still valid by id even if the current filter page doesn't include it.
                OwnedPackageId = keepId;
                OnPropertyChanged(nameof(CanSave));
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    partial void OnSelectedOwnedChanged(PackageListEntry? value)
    {
        if (_suppressSelection)
            return;

        OwnedPackageId = value?.PackageId ?? 0;
        if (value is not null && !string.Equals(OwnedQuery, value.VarName, StringComparison.Ordinal))
        {
            // Reflect the pick in the search box without kicking off another library query.
            _suppressSearch = true;
            try { OwnedQuery = value.VarName; }
            finally { _suppressSearch = false; }
        }
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnMissingRefChanged(string? value)
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task CopyMissingAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(MissingRef) || clipboard is null)
            return;
        await clipboard.SetTextAsync(MissingRef.Trim(), cancellationToken).ConfigureAwait(true);
        ClipboardStatus = "Copied";
    }

    [RelayCommand]
    private async Task CopyOwnedAsync(CancellationToken cancellationToken = default)
    {
        var text = SelectedOwned?.VarName;
        if (string.IsNullOrWhiteSpace(text) || clipboard is null)
            return;
        await clipboard.SetTextAsync(text, cancellationToken).ConfigureAwait(true);
        ClipboardStatus = "Copied owned name";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(MissingRef) || OwnedPackageId == 0)
        {
            StatusMessage = "Choose an owned package first.";
            return;
        }
        try
        {
            var result = await aliases.SetAsync(MissingRef!, OwnedPackageId, cancellationToken).ConfigureAwait(true);
            StatusMessage = result.IsSuccess ? "Alias saved" : result.Error.Message;
            if (result.IsSuccess)
            {
                OnSaved?.Invoke();
                close?.Invoke();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }
}
