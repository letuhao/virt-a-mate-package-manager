using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using VirtaMatePackageManager.Application.Commands;
using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Application.Services;

namespace VirtaMatePackageManager.Presentation.ViewModels;

/// <summary>
/// ViewModel for installation target management dialog.
/// </summary>
public class InstallationTargetManagementViewModel : ViewModelBase
{
    private readonly IInstallationTargetService _installationTargetService;
    
    private InstallationTargetDto? _selectedTarget;
    private string _newTargetName = string.Empty;
    private string _newTargetPath = string.Empty;
    private string _newTargetProfileName = string.Empty;
    private string _newTargetDescription = string.Empty;
    private bool _isLoading;
    private string _statusMessage = string.Empty;

    public InstallationTargetManagementViewModel(IInstallationTargetService installationTargetService)
    {
        _installationTargetService = installationTargetService ?? throw new ArgumentNullException(nameof(installationTargetService));
        
        InstallationTargets = new ObservableCollection<InstallationTargetDto>();
        
        AddTargetCommand = new RelayCommand(async _ => await AddTargetAsync(), _ => !IsLoading && CanAddTarget());
        UpdateTargetCommand = new RelayCommand(async _ => await UpdateTargetAsync(), _ => !IsLoading && SelectedTarget != null);
        DeleteTargetCommand = new RelayCommand(async _ => await DeleteTargetAsync(), _ => !IsLoading && SelectedTarget != null);
        SetActiveCommand = new RelayCommand(async _ => await SetActiveTargetAsync(), _ => !IsLoading && SelectedTarget != null);
        RefreshCommand = new RelayCommand(async _ => await LoadTargetsAsync(), _ => !IsLoading);
        BrowsePathCommand = new RelayCommand(_ => BrowseForPath(), _ => !IsLoading);
    }

    /// <summary>
    /// Collection of installation targets.
    /// </summary>
    public ObservableCollection<InstallationTargetDto> InstallationTargets { get; }

    /// <summary>
    /// Currently selected installation target.
    /// </summary>
    public InstallationTargetDto? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                if (value != null)
                {
                    NewTargetName = value.Name;
                    NewTargetPath = value.Path;
                    NewTargetProfileName = value.ProfileName ?? string.Empty;
                    NewTargetDescription = value.Description ?? string.Empty;
                }
                ((RelayCommand)UpdateTargetCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteTargetCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SetActiveCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Name for new installation target.
    /// </summary>
    public string NewTargetName
    {
        get => _newTargetName;
        set
        {
            if (SetProperty(ref _newTargetName, value))
            {
                ((RelayCommand)AddTargetCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Path for new installation target.
    /// </summary>
    public string NewTargetPath
    {
        get => _newTargetPath;
        set
        {
            if (SetProperty(ref _newTargetPath, value))
            {
                ((RelayCommand)AddTargetCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Profile name for new installation target.
    /// </summary>
    public string NewTargetProfileName
    {
        get => _newTargetProfileName;
        set => SetProperty(ref _newTargetProfileName, value);
    }

    /// <summary>
    /// Description for new installation target.
    /// </summary>
    public string NewTargetDescription
    {
        get => _newTargetDescription;
        set => SetProperty(ref _newTargetDescription, value);
    }

    /// <summary>
    /// Indicates whether an operation is in progress.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                ((RelayCommand)AddTargetCommand).RaiseCanExecuteChanged();
                ((RelayCommand)UpdateTargetCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteTargetCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SetActiveCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
                ((RelayCommand)BrowsePathCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Status message.
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// Command to add a new installation target.
    /// </summary>
    public ICommand AddTargetCommand { get; }

    /// <summary>
    /// Command to update selected installation target.
    /// </summary>
    public ICommand UpdateTargetCommand { get; }

    /// <summary>
    /// Command to delete selected installation target.
    /// </summary>
    public ICommand DeleteTargetCommand { get; }

    /// <summary>
    /// Command to set selected target as active.
    /// </summary>
    public ICommand SetActiveCommand { get; }

    /// <summary>
    /// Command to refresh installation targets.
    /// </summary>
    public ICommand RefreshCommand { get; }

    /// <summary>
    /// Command to browse for installation target path.
    /// </summary>
    public ICommand BrowsePathCommand { get; }

    /// <summary>
    /// Loads installation targets.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadTargetsAsync();
    }

    /// <summary>
    /// Loads all installation targets.
    /// </summary>
    private async Task LoadTargetsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading installation targets...";

            var result = await _installationTargetService.GetAllTargetsAsync(CancellationToken.None);

            if (result.IsSuccess)
            {
                InstallationTargets.Clear();
                foreach (var target in result.Value!)
                {
                    InstallationTargets.Add(target);
                }

                StatusMessage = $"Loaded {InstallationTargets.Count} installation targets";
            }
            else
            {
                StatusMessage = $"Error: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Exception: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Adds a new installation target.
    /// </summary>
    private async Task AddTargetAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Adding installation target...";

            var command = new AddInstallationTargetCommand(
                Name: NewTargetName,
                Path: NewTargetPath,
                ProfileName: string.IsNullOrWhiteSpace(NewTargetProfileName) ? null : NewTargetProfileName,
                Description: string.IsNullOrWhiteSpace(NewTargetDescription) ? null : NewTargetDescription
            );

            var result = await _installationTargetService.AddInstallationTargetAsync(command, CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = "Installation target added successfully";
                
                // Clear form
                NewTargetName = string.Empty;
                NewTargetPath = string.Empty;
                NewTargetProfileName = string.Empty;
                NewTargetDescription = string.Empty;
                
                // Refresh list
                await LoadTargetsAsync();
            }
            else
            {
                StatusMessage = $"Error: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Exception: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Updates the selected installation target.
    /// </summary>
    private async Task UpdateTargetAsync()
    {
        if (SelectedTarget == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Updating installation target...";

            var command = new UpdateInstallationTargetCommand(
                Id: SelectedTarget.Id,
                Name: NewTargetName,
                Path: NewTargetPath,
                ProfileName: string.IsNullOrWhiteSpace(NewTargetProfileName) ? null : NewTargetProfileName,
                Description: string.IsNullOrWhiteSpace(NewTargetDescription) ? null : NewTargetDescription
            );

            var result = await _installationTargetService.UpdateInstallationTargetAsync(command, CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = "Installation target updated successfully";
                await LoadTargetsAsync();
                
                // Re-select the updated target
                if (SelectedTarget != null)
                {
                    var updated = InstallationTargets.FirstOrDefault(t => t.Id == SelectedTarget.Id);
                    SelectedTarget = updated;
                }
            }
            else
            {
                StatusMessage = $"Error: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Exception: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Deletes the selected installation target.
    /// </summary>
    private async Task DeleteTargetAsync()
    {
        if (SelectedTarget == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Deleting installation target...";

            var result = await _installationTargetService.DeleteInstallationTargetAsync(SelectedTarget.Id, CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = "Installation target deleted successfully";
                SelectedTarget = null;
                await LoadTargetsAsync();
            }
            else
            {
                StatusMessage = $"Error: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Exception: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Sets the selected target as active.
    /// </summary>
    private async Task SetActiveTargetAsync()
    {
        if (SelectedTarget == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Setting active target...";

            var result = await _installationTargetService.SetActiveTargetAsync(SelectedTarget.Id, CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = "Active target set successfully";
                await LoadTargetsAsync();
                
                // Re-select the updated target
                var updated = InstallationTargets.FirstOrDefault(t => t.Id == SelectedTarget.Id);
                SelectedTarget = updated;
            }
            else
            {
                StatusMessage = $"Error: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Exception: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Checks if installation target can be added.
    /// </summary>
    private bool CanAddTarget()
    {
        return !string.IsNullOrWhiteSpace(NewTargetName) && 
               !string.IsNullOrWhiteSpace(NewTargetPath) &&
               Directory.Exists(NewTargetPath);
    }

    /// <summary>
    /// Opens folder browser dialog to select installation target path.
    /// </summary>
    private void BrowseForPath()
    {
        try
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Virt-a-Mate installation folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (!string.IsNullOrWhiteSpace(NewTargetPath) && Directory.Exists(NewTargetPath))
            {
                dialog.SelectedPath = NewTargetPath;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                NewTargetPath = dialog.SelectedPath;
                StatusMessage = $"Selected: {NewTargetPath}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error browsing folder: {ex.Message}";
        }
    }
}

