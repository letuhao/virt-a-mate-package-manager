using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Domain.Dependencies;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>Edit meta.json fields + dependency refs for one package (paged/filterable deps list).</summary>
public sealed partial class EditMetaViewModel : ObservableObject
{
    public const int DepsPageSize = 50;

    private readonly IVarMetaEditService _meta;
    private readonly Action? _close;
    private readonly List<MetaDepRow> _allDeps = [];
    private bool _suppressDirty;

    public EditMetaViewModel(IVarMetaEditService meta, long packageId, Action? onSaved = null, Action? close = null)
    {
        _meta = meta;
        PackageId = packageId;
        OnSaved = onSaved;
        _close = close;
    }

    public long PackageId { get; }
    public Action? OnSaved { get; set; }

    [ObservableProperty] private string _varName = "";
    [ObservableProperty] private string _absolutePath = "";
    [ObservableProperty] private long _varFileId;
    [ObservableProperty] private string _creatorName = "";
    [ObservableProperty] private string _packageName = "";
    [ObservableProperty] private string _licenseType = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _programVersion = "";
    [ObservableProperty] private string _depsFilter = "";
    [ObservableProperty] private string _newDepRef = "";
    [ObservableProperty] private int _depsPageNumber = 1;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private MetaDepRow? _selectedDep;

    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<MetaDepRow> VisibleDeps { get; } = [];

    public int DepsTotalCount => FilteredDeps().Count;
    public int DepsPageCount => Math.Max(1, (int)Math.Ceiling(DepsTotalCount / (double)DepsPageSize));
    public string DepsSummary => DepsTotalCount == 0
        ? "0 dependencies"
        : $"{VisibleDeps.Count} shown · page {DepsPageNumber}/{DepsPageCount} · {DepsTotalCount} total";
    public bool CanSave => !IsBusy && IsDirty && !HasInvalidDeps;
    public bool HasInvalidDeps => _allDeps.Any(d => !d.IsValid);
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public bool HasSuccessStatus => HasStatus && !IsError;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = null;
        IsError = false;
        _suppressDirty = true;
        try
        {
            var draft = await _meta.LoadAsync(PackageId, cancellationToken: cancellationToken).ConfigureAwait(true);
            if (draft.IsFailure)
            {
                StatusMessage = draft.Error.Message;
                IsError = true;
                return;
            }

            var d = draft.Value;
            VarName = d.VarName;
            AbsolutePath = d.AbsolutePath;
            VarFileId = d.VarFileId;
            CreatorName = d.CreatorName ?? "";
            PackageName = d.PackageName ?? "";
            LicenseType = d.LicenseType ?? "";
            Description = d.Description ?? "";
            ProgramVersion = d.ProgramVersion ?? "";
            Warnings.Clear();
            foreach (var w in d.Warnings)
                Warnings.Add(w);

            _allDeps.Clear();
            foreach (var r in d.DependencyRefs)
                _allDeps.Add(MetaDepRow.FromRaw(r, MarkDirty));
            DepsPageNumber = 1;
            RefreshVisibleDeps();
            IsDirty = false;
        }
        finally
        {
            _suppressDirty = false;
            IsBusy = false;
            NotifySaveState();
        }
    }

    [RelayCommand]
    private void AddDep()
    {
        var raw = NewDepRef.Trim();
        if (raw.Length == 0)
            return;
        if (_allDeps.Any(d => string.Equals(d.Ref, raw, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "That dependency is already in the list.";
            IsError = true;
            OnPropertyChanged(nameof(HasStatus));
            return;
        }

        StatusMessage = null;
        IsError = false;
        var row = MetaDepRow.FromRaw(raw, MarkDirty);
        _allDeps.Add(row);
        NewDepRef = "";
        MarkDirty();
        // Show the newly added row (last page when unfiltered).
        if (DepsFilter.Trim().Length == 0)
            DepsPageNumber = Math.Max(1, (int)Math.Ceiling(_allDeps.Count / (double)DepsPageSize));
        RefreshVisibleDeps();
        SelectedDep = row;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveDep))]
    private void RemoveDep()
    {
        if (SelectedDep is null)
            return;
        _allDeps.Remove(SelectedDep);
        SelectedDep = null;
        MarkDirty();
        RefreshVisibleDeps();
    }

    private bool CanRemoveDep => SelectedDep is not null;

    [RelayCommand]
    private void DepsPreviousPage()
    {
        if (DepsPageNumber <= 1)
            return;
        DepsPageNumber--;
        RefreshVisibleDeps();
    }

    [RelayCommand]
    private void DepsNextPage()
    {
        if (DepsPageNumber >= DepsPageCount)
            return;
        DepsPageNumber++;
        RefreshVisibleDeps();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSave)
            return;

        IsBusy = true;
        StatusMessage = null;
        IsError = false;
        NotifySaveState();
        try
        {
            var request = new VarMetaEditRequest(
                PackageId,
                VarFileId,
                CreatorName,
                PackageName,
                LicenseType,
                Description,
                ProgramVersion,
                _allDeps.Select(d => d.Ref).ToList());
            var result = await _meta.SaveAsync(request, cancellationToken).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                IsError = true;
                return;
            }

            IsDirty = false;
            StatusMessage = $"Saved · {result.Value.DependencyCount} dependencies · previous file in Trash.";
            OnSaved?.Invoke();
            _close?.Invoke();
        }
        finally
        {
            IsBusy = false;
            NotifySaveState();
        }
    }

    [RelayCommand]
    private void Cancel() => _close?.Invoke();

    partial void OnCreatorNameChanged(string value) => MarkDirty();
    partial void OnPackageNameChanged(string value) => MarkDirty();
    partial void OnLicenseTypeChanged(string value) => MarkDirty();
    partial void OnDescriptionChanged(string value) => MarkDirty();
    partial void OnProgramVersionChanged(string value) => MarkDirty();
    partial void OnDepsFilterChanged(string value)
    {
        DepsPageNumber = 1;
        RefreshVisibleDeps();
    }
    partial void OnSelectedDepChanged(MetaDepRow? value) => RemoveDepCommand.NotifyCanExecuteChanged();
    partial void OnStatusMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(HasSuccessStatus));
    }

    partial void OnIsErrorChanged(bool value) => OnPropertyChanged(nameof(HasSuccessStatus));

    private void MarkDirty()
    {
        if (_suppressDirty)
            return;
        IsDirty = true;
        NotifySaveState();
    }

    private void NotifySaveState()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasInvalidDeps));
        OnPropertyChanged(nameof(DepsTotalCount));
        OnPropertyChanged(nameof(DepsPageCount));
        OnPropertyChanged(nameof(DepsSummary));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(HasSuccessStatus));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private List<MetaDepRow> FilteredDeps()
    {
        var q = DepsFilter.Trim();
        if (q.Length == 0)
            return _allDeps.ToList();
        return _allDeps
            .Where(d => d.Ref.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void RefreshVisibleDeps()
    {
        var filtered = FilteredDeps();
        var pageCount = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)DepsPageSize));
        if (DepsPageNumber > pageCount)
            DepsPageNumber = pageCount;
        if (DepsPageNumber < 1)
            DepsPageNumber = 1;
        VisibleDeps.Clear();
        foreach (var row in filtered.Skip((DepsPageNumber - 1) * DepsPageSize).Take(DepsPageSize))
            VisibleDeps.Add(row);
        NotifySaveState();
    }
}

/// <summary>One editable dependency row with live validation.</summary>
public sealed partial class MetaDepRow : ObservableObject
{
    private readonly Action? _onChanged;

    public MetaDepRow(Action? onChanged = null) => _onChanged = onChanged;

    [ObservableProperty] private string _ref = "";
    [ObservableProperty] private bool _isValid = true;
    [ObservableProperty] private string? _error;

    public static MetaDepRow FromRaw(string raw, Action? onChanged = null)
    {
        var row = new MetaDepRow(onChanged) { Ref = raw };
        row.Validate();
        return row;
    }

    partial void OnRefChanged(string value)
    {
        Validate();
        _onChanged?.Invoke();
    }

    private void Validate()
    {
        var parsed = DependencyRef.Parse(Ref ?? "");
        IsValid = parsed.IsSuccess;
        Error = parsed.IsSuccess ? null : parsed.Error.Message;
    }
}
