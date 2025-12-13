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
using VirtaMatePackageManager.Presentation.ViewModels.Messaging;

namespace VirtaMatePackageManager.Presentation.ViewModels;

/// <summary>
/// ViewModel for repository management dialog.
/// </summary>
public class RepositoryManagementViewModel : ViewModelBase
{
    private readonly IRepositoryService _repositoryService;
    
    private RepositoryDto? _selectedRepository;
    private string _newRepositoryName = string.Empty;
    private string _newRepositoryPath = string.Empty;
    private string _newRepositoryDescription = string.Empty;
    private int _newRepositoryPriority = 0;
    private bool _isLoading;
    private string _statusMessage = string.Empty;

    public RepositoryManagementViewModel(IRepositoryService repositoryService)
    {
        _repositoryService = repositoryService ?? throw new ArgumentNullException(nameof(repositoryService));
        
        Repositories = new ObservableCollection<RepositoryDto>();
        
        AddRepositoryCommand = new RelayCommand(async _ => await AddRepositoryAsync(), _ => !IsLoading && CanAddRepository());
        UpdateRepositoryCommand = new RelayCommand(async _ => await UpdateRepositoryAsync(), _ => !IsLoading && SelectedRepository != null);
        DeleteRepositoryCommand = new RelayCommand(async _ => await DeleteRepositoryAsync(), _ => !IsLoading && SelectedRepository != null);
        EnableRepositoryCommand = new RelayCommand(async _ => await ToggleRepositoryEnabledAsync(), _ => !IsLoading && SelectedRepository != null);
        RefreshCommand = new RelayCommand(async _ => await LoadRepositoriesAsync(), _ => !IsLoading);
        BrowsePathCommand = new RelayCommand(_ => BrowseForPath(), _ => !IsLoading);
    }

    /// <summary>
    /// Collection of repositories.
    /// </summary>
    public ObservableCollection<RepositoryDto> Repositories { get; }

    /// <summary>
    /// Currently selected repository.
    /// </summary>
    public RepositoryDto? SelectedRepository
    {
        get => _selectedRepository;
        set
        {
            if (SetProperty(ref _selectedRepository, value))
            {
                if (value != null)
                {
                    NewRepositoryName = value.Name;
                    NewRepositoryPath = value.Path;
                    NewRepositoryDescription = value.Description ?? string.Empty;
                    NewRepositoryPriority = value.Priority;
                }
                ((RelayCommand)UpdateRepositoryCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteRepositoryCommand).RaiseCanExecuteChanged();
                ((RelayCommand)EnableRepositoryCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Name for new repository.
    /// </summary>
    public string NewRepositoryName
    {
        get => _newRepositoryName;
        set
        {
            if (SetProperty(ref _newRepositoryName, value))
            {
                ((RelayCommand)AddRepositoryCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Path for new repository.
    /// </summary>
    public string NewRepositoryPath
    {
        get => _newRepositoryPath;
        set
        {
            if (SetProperty(ref _newRepositoryPath, value))
            {
                ((RelayCommand)AddRepositoryCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Description for new repository.
    /// </summary>
    public string NewRepositoryDescription
    {
        get => _newRepositoryDescription;
        set => SetProperty(ref _newRepositoryDescription, value);
    }

    /// <summary>
    /// Priority for new repository.
    /// </summary>
    public int NewRepositoryPriority
    {
        get => _newRepositoryPriority;
        set => SetProperty(ref _newRepositoryPriority, value);
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
                ((RelayCommand)AddRepositoryCommand).RaiseCanExecuteChanged();
                ((RelayCommand)UpdateRepositoryCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteRepositoryCommand).RaiseCanExecuteChanged();
                ((RelayCommand)EnableRepositoryCommand).RaiseCanExecuteChanged();
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
    /// Command to add a new repository.
    /// </summary>
    public ICommand AddRepositoryCommand { get; }

    /// <summary>
    /// Command to update selected repository.
    /// </summary>
    public ICommand UpdateRepositoryCommand { get; }

    /// <summary>
    /// Command to delete selected repository.
    /// </summary>
    public ICommand DeleteRepositoryCommand { get; }

    /// <summary>
    /// Command to toggle repository enabled state.
    /// </summary>
    public ICommand EnableRepositoryCommand { get; }

    /// <summary>
    /// Command to refresh repositories.
    /// </summary>
    public ICommand RefreshCommand { get; }

    /// <summary>
    /// Command to browse for repository path.
    /// </summary>
    public ICommand BrowsePathCommand { get; }

    /// <summary>
    /// Loads repositories.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadRepositoriesAsync();
    }

    /// <summary>
    /// Loads all repositories.
    /// </summary>
    private async Task LoadRepositoriesAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading repositories...";

            var result = await _repositoryService.GetAllRepositoriesAsync(CancellationToken.None);

            if (result.IsSuccess)
            {
                Repositories.Clear();
                foreach (var repository in result.Value!)
                {
                    Repositories.Add(repository);
                }

                StatusMessage = $"Loaded {Repositories.Count} repositories";
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
    /// Adds a new repository.
    /// </summary>
    private async Task AddRepositoryAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Adding repository...";

            var command = new AddRepositoryCommand(
                Name: NewRepositoryName,
                Path: NewRepositoryPath,
                Description: string.IsNullOrWhiteSpace(NewRepositoryDescription) ? null : NewRepositoryDescription,
                Priority: NewRepositoryPriority
            );

            var result = await _repositoryService.AddRepositoryAsync(command, CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = "Repository added successfully";
                
                // Clear form
                NewRepositoryName = string.Empty;
                NewRepositoryPath = string.Empty;
                NewRepositoryDescription = string.Empty;
                NewRepositoryPriority = 0;
                
                // Refresh list
                await LoadRepositoriesAsync();
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
    /// Updates the selected repository.
    /// </summary>
    private async Task UpdateRepositoryAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Updating repository...";

            var command = new UpdateRepositoryCommand(
                Id: SelectedRepository.Id,
                Name: NewRepositoryName,
                Path: NewRepositoryPath,
                Description: string.IsNullOrWhiteSpace(NewRepositoryDescription) ? null : NewRepositoryDescription,
                Priority: NewRepositoryPriority
            );

            var result = await _repositoryService.UpdateRepositoryAsync(command, CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = "Repository updated successfully";
                await LoadRepositoriesAsync();
                
                // Re-select the updated repository
                if (SelectedRepository != null)
                {
                    var updated = Repositories.FirstOrDefault(r => r.Id == SelectedRepository.Id);
                    SelectedRepository = updated;
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
    /// Deletes the selected repository.
    /// </summary>
    private async Task DeleteRepositoryAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Deleting repository...";

            var result = await _repositoryService.DeleteRepositoryAsync(SelectedRepository.Id, CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = "Repository deleted successfully";
                SelectedRepository = null;
                await LoadRepositoriesAsync();
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
    /// Toggles the enabled state of the selected repository.
    /// </summary>
    private async Task ToggleRepositoryEnabledAsync()
    {
        if (SelectedRepository == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Updating repository state...";

            var newEnabledState = !SelectedRepository.Enabled;
            var result = await _repositoryService.EnableRepositoryAsync(SelectedRepository.Id, newEnabledState, CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = $"Repository {(newEnabledState ? "enabled" : "disabled")} successfully";
                await LoadRepositoriesAsync();
                
                // Re-select the updated repository
                var updated = Repositories.FirstOrDefault(r => r.Id == SelectedRepository.Id);
                SelectedRepository = updated;
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
    /// Checks if repository can be added.
    /// </summary>
    private bool CanAddRepository()
    {
        return !string.IsNullOrWhiteSpace(NewRepositoryName) && 
               !string.IsNullOrWhiteSpace(NewRepositoryPath) &&
               Directory.Exists(NewRepositoryPath);
    }

    /// <summary>
    /// Opens folder browser dialog to select repository path.
    /// </summary>
    private void BrowseForPath()
    {
        try
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select repository folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (!string.IsNullOrWhiteSpace(NewRepositoryPath) && Directory.Exists(NewRepositoryPath))
            {
                dialog.SelectedPath = NewRepositoryPath;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                NewRepositoryPath = dialog.SelectedPath;
                StatusMessage = $"Selected: {NewRepositoryPath}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error browsing folder: {ex.Message}";
        }
    }
}

