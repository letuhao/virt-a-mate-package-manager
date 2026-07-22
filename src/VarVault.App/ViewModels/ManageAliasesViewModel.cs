using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.App.Services;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>
/// FormMissingVars-style alias review: list existing aliases (unlink), add/link a missing ref to an owned
/// package, and copy package names. (Legacy parity for missing-deps repair.)
/// </summary>
public sealed partial class ManageAliasesViewModel(
    IAliasService aliases,
    ILibraryQueryService? library = null,
    IClipboard? clipboard = null,
    Action? close = null) : ObservableObject
{
    private int _searchEpoch;
    private bool _suppressSearch;
    private bool _suppressSelection;
    private List<AliasDto> _all = [];

    /// <summary>Invoked after any successful set/remove so the Missing deps screen can refresh.</summary>
    public Action? OnChanged { get; init; }

    public ObservableCollection<AliasDto> Items { get; } = [];
    public ObservableCollection<PackageListEntry> OwnedMatches { get; } = [];

    [ObservableProperty] private string? _filterText;
    [ObservableProperty] private AliasDto? _selectedAlias;
    [ObservableProperty] private string? _missingRef;
    [ObservableProperty] private string? _ownedQuery;
    [ObservableProperty] private PackageListEntry? _selectedOwned;
    [ObservableProperty] private long _ownedPackageId;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _clipboardStatus;
    [ObservableProperty] private bool _isBusy;

    public bool CanLink => !string.IsNullOrWhiteSpace(MissingRef) && OwnedPackageId != 0 && !IsBusy;
    public bool CanUnlink => SelectedAlias is not null && !IsBusy;
    public bool HasAliases => Items.Count > 0;
    public string AliasSummary => _all.Count == 0
        ? "No aliases saved yet."
        : $"{_all.Count} alias{(_all.Count == 1 ? "" : "es")} · select one to unlink or edit.";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            _all = (await aliases.ListAsync(cancellationToken).ConfigureAwait(true)).OrderBy(a => a.MissingRef, StringComparer.OrdinalIgnoreCase).ToList();
            ApplyFilter();
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    partial void OnFilterTextChanged(string? value) => ApplyFilter();

    partial void OnMissingRefChanged(string? value) => NotifyCommands();

    partial void OnOwnedPackageIdChanged(long value) => NotifyCommands();

    partial void OnOwnedQueryChanged(string? value)
    {
        if (_suppressSearch)
            return;
        SearchTask = SearchOwnedAsync(value);
    }

    partial void OnSelectedOwnedChanged(PackageListEntry? value)
    {
        if (_suppressSelection)
            return;
        OwnedPackageId = value?.PackageId ?? 0;
        if (value is not null && !string.Equals(OwnedQuery, value.VarName, StringComparison.Ordinal))
        {
            _suppressSearch = true;
            try { OwnedQuery = value.VarName; }
            finally { _suppressSearch = false; }
        }
        NotifyCommands();
    }

    partial void OnSelectedAliasChanged(AliasDto? value)
    {
        if (value is null)
            return;
        MissingRef = value.MissingRef;
        if (!string.IsNullOrWhiteSpace(value.ResolvedVarName))
            OwnedQuery = value.ResolvedVarName;
        NotifyCommands();
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();

    /// <summary>Exposed so tests can await owned-package search settle.</summary>
    public Task? SearchTask { get; private set; }

    private void ApplyFilter()
    {
        var keepId = SelectedAlias?.Id;
        Items.Clear();
        IEnumerable<AliasDto> src = _all;
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var q = FilterText.Trim();
            src = src.Where(a =>
                a.MissingRef.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (a.ResolvedVarName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        foreach (var a in src)
            Items.Add(a);
        SelectedAlias = keepId is { } id ? Items.FirstOrDefault(a => a.Id == id) : null;
        OnPropertyChanged(nameof(HasAliases));
        OnPropertyChanged(nameof(AliasSummary));
        NotifyCommands();
    }

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
            var page = await library.GetPageAsync(new LibraryQuery(Skip: 0, Take: 60, SearchText: text)).ConfigureAwait(true);
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
                OwnedPackageId = keepId;
                NotifyCommands();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanLink))]
    private async Task LinkAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(MissingRef) || OwnedPackageId == 0)
            return;
        IsBusy = true;
        try
        {
            var result = await aliases.SetAsync(MissingRef.Trim(), OwnedPackageId, cancellationToken).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                StatusMessage = result.Error.Message;
                return;
            }
            StatusMessage = $"Linked {MissingRef.Trim()} → {SelectedOwned?.VarName ?? OwnedPackageId.ToString()}";
            OnChanged?.Invoke();
            await LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Link failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUnlink))]
    private async Task UnlinkAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedAlias is null)
            return;
        var id = SelectedAlias.Id;
        var missing = SelectedAlias.MissingRef;
        IsBusy = true;
        try
        {
            var result = await aliases.RemoveAsync(id, cancellationToken).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                StatusMessage = result.Error.Message;
                return;
            }
            StatusMessage = $"Unlinked {missing}";
            SelectedAlias = null;
            OnChanged?.Invoke();
            await LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unlink failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    [RelayCommand]
    private async Task CopyMissingAsync(CancellationToken cancellationToken = default)
    {
        var text = SelectedAlias?.MissingRef ?? MissingRef;
        if (string.IsNullOrWhiteSpace(text) || clipboard is null)
            return;
        await clipboard.SetTextAsync(text.Trim(), cancellationToken).ConfigureAwait(true);
        ClipboardStatus = "Copied missing ref";
    }

    [RelayCommand]
    private async Task CopyTargetAsync(CancellationToken cancellationToken = default)
    {
        var text = SelectedAlias?.ResolvedVarName ?? SelectedOwned?.VarName;
        if (string.IsNullOrWhiteSpace(text) || clipboard is null)
            return;
        await clipboard.SetTextAsync(text, cancellationToken).ConfigureAwait(true);
        ClipboardStatus = "Copied owned package name";
    }

    [RelayCommand]
    private void Close() => close?.Invoke();

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanLink));
        OnPropertyChanged(nameof(CanUnlink));
        LinkCommand.NotifyCanExecuteChanged();
        UnlinkCommand.NotifyCanExecuteChanged();
    }
}
