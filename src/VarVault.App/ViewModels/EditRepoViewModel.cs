using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Repositories;

namespace VarVault.App.ViewModels;

/// <summary>
/// Edit an existing repository: rename it and/or re-point it to a new folder. Re-point goes through the
/// volume-serial guard (a different drive is refused, not silently mis-bound). On success, <c>onSaved</c> lets the
/// Repositories screen refresh. (Completes the previously stubbed "Edit" button.)
/// </summary>
public sealed partial class EditRepoViewModel : ObservableObject
{
    private readonly IRepositoryService _repositories;
    private readonly Action? _onSaved;
    private readonly Guid _id;
    private readonly string _originalName;
    private readonly string _originalPath;

    public EditRepoViewModel(IRepositoryService repositories, RepositoryInfo repo, Action? onSaved = null)
    {
        _repositories = repositories;
        _onSaved = onSaved;
        _id = repo.Id;
        _originalName = repo.Name;
        _originalPath = repo.MountPath;
        Name = repo.Name;
        Path = repo.MountPath;
    }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _saved;

    /// <summary>Folder-picker hook the view sets to the real StorageProvider.</summary>
    public Func<Task<string?>>? FolderPicker { get; set; }

    /// <summary>"Browse…" → pick the new repository folder.</summary>
    [RelayCommand]
    public async Task BrowseAsync()
    {
        if (FolderPicker is null)
            return;
        var picked = await FolderPicker().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(picked))
            Path = picked;
    }

    /// <summary>Apply rename + re-point (only what changed). Surfaces the re-point serial guard on failure.</summary>
    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        IsError = false;
        Message = null;

        var nameChanged = !string.IsNullOrWhiteSpace(Name)
            && !string.Equals(Name.Trim(), _originalName, StringComparison.Ordinal);
        var pathChanged = !string.IsNullOrWhiteSpace(Path)
            && !PathsEqual(Path, _originalPath);

        if (!nameChanged && !pathChanged)
        {
            Message = "Nothing to change.";
            return;
        }

        if (nameChanged)
            await _repositories.RenameAsync(_id, Name.Trim(), cancellationToken).ConfigureAwait(true);

        if (pathChanged)
        {
            var r = await _repositories.RepointAsync(_id, Path.Trim(), cancellationToken).ConfigureAwait(true);
            if (r.IsFailure)
            {
                IsError = true;
                Message = r.Error.Message;   // e.g. "different serial — refusing to re-point" or "folder not found"
                _onSaved?.Invoke();          // still refresh: the rename above may have applied
                return;
            }
        }

        Saved = true;
        Message = "Saved.";
        _onSaved?.Invoke();
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            System.IO.Path.GetFullPath(a).TrimEnd(System.IO.Path.DirectorySeparatorChar),
            System.IO.Path.GetFullPath(b).TrimEnd(System.IO.Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
