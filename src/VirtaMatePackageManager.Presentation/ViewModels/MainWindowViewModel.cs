using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Application.Queries;
using VirtaMatePackageManager.Application.Services;
using VirtaMatePackageManager.Presentation.ViewModels.Messaging;

namespace VirtaMatePackageManager.Presentation.ViewModels;

/// <summary>
/// ViewModel for the main window.
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private readonly IVarPackageService _varPackageService;
    private readonly IRepositoryService _repositoryService;
    private readonly ISearchService _searchService;
    private readonly IInstallationService _installationService;
    private readonly IInstallationTargetService _installationTargetService;

    private string _statusMessage = "Ready";
    private bool _isLoading;
    private string _searchText = string.Empty;
    private int _totalVarPackages;
    private RepositoryDto? _selectedRepository;
    private VarPackageDto? _selectedVarPackage;
    private MessageViewModel? _currentMessage;

    public MainWindowViewModel(
        IVarPackageService varPackageService,
        IRepositoryService repositoryService,
        ISearchService searchService,
        IInstallationService installationService,
        IInstallationTargetService installationTargetService)
    {
        _varPackageService = varPackageService ?? throw new ArgumentNullException(nameof(varPackageService));
        _repositoryService = repositoryService ?? throw new ArgumentNullException(nameof(repositoryService));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
        _installationTargetService = installationTargetService ?? throw new ArgumentNullException(nameof(installationTargetService));

        VarPackages = new ObservableCollection<VarPackageDto>();
        Repositories = new ObservableCollection<RepositoryDto>();

        SearchCommand = new RelayCommand(async _ => await SearchAsync(), _ => !IsLoading);
        RefreshRepositoriesCommand = new RelayCommand(async _ => await LoadRepositoriesAsync(), _ => !IsLoading);
        RefreshVarPackagesCommand = new RelayCommand(async _ => await LoadVarPackagesAsync(), _ => !IsLoading);
        InstallVarPackageCommand = new RelayCommand(async _ => await InstallSelectedVarPackageAsync(), _ => !IsLoading && SelectedVarPackage != null);
        UninstallVarPackageCommand = new RelayCommand(async _ => await UninstallSelectedVarPackageAsync(), _ => !IsLoading && SelectedVarPackage != null && SelectedVarPackage.InstallationStatus != null);
        ShowVarPackageDetailsCommand = new RelayCommand(_ => ShowVarPackageDetails(), _ => !IsLoading && SelectedVarPackage != null);
    }

    /// <summary>
    /// Collection of VAR packages displayed in the UI.
    /// </summary>
    public ObservableCollection<VarPackageDto> VarPackages { get; }

    /// <summary>
    /// Collection of repositories.
    /// </summary>
    public ObservableCollection<RepositoryDto> Repositories { get; }

    /// <summary>
    /// Current status message displayed to the user.
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
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
                // Update command states when loading state changes
                ((RelayCommand)SearchCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RefreshRepositoriesCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RefreshVarPackagesCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Search text entered by the user.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    /// <summary>
    /// Total number of VAR packages.
    /// </summary>
    public int TotalVarPackages
    {
        get => _totalVarPackages;
        set => SetProperty(ref _totalVarPackages, value);
    }

    /// <summary>
    /// Currently selected repository.
    /// </summary>
    public RepositoryDto? SelectedRepository
    {
        get => _selectedRepository;
        set => SetProperty(ref _selectedRepository, value);
    }

    /// <summary>
    /// Currently selected VAR package.
    /// </summary>
    public VarPackageDto? SelectedVarPackage
    {
        get => _selectedVarPackage;
        set
        {
            if (SetProperty(ref _selectedVarPackage, value))
            {
                ((RelayCommand)InstallVarPackageCommand).RaiseCanExecuteChanged();
                ((RelayCommand)UninstallVarPackageCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ShowVarPackageDetailsCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Current message to display to the user.
    /// </summary>
    public MessageViewModel? CurrentMessage
    {
        get => _currentMessage;
        set => SetProperty(ref _currentMessage, value);
    }

    /// <summary>
    /// Command to search VAR packages.
    /// </summary>
    public ICommand SearchCommand { get; }

    /// <summary>
    /// Command to refresh repositories.
    /// </summary>
    public ICommand RefreshRepositoriesCommand { get; }

    /// <summary>
    /// Command to refresh VAR packages.
    /// </summary>
    public ICommand RefreshVarPackagesCommand { get; }

    /// <summary>
    /// Command to install selected VAR package.
    /// </summary>
    public ICommand InstallVarPackageCommand { get; }

    /// <summary>
    /// Command to uninstall selected VAR package.
    /// </summary>
    public ICommand UninstallVarPackageCommand { get; }

    /// <summary>
    /// Command to show VAR package details.
    /// </summary>
    public ICommand ShowVarPackageDetailsCommand { get; }

    /// <summary>
    /// Loads initial data.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading initial data...";

            await Task.WhenAll(
                LoadRepositoriesAsync(),
                LoadVarPackagesAsync()
            );

            StatusMessage = "Ready";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads all repositories.
    /// </summary>
    private async Task LoadRepositoriesAsync()
    {
        try
        {
            var result = await _repositoryService.GetAllRepositoriesAsync(CancellationToken.None);

            if (result.IsSuccess)
            {
                Repositories.Clear();
                foreach (var repository in result.Value!)
                {
                    Repositories.Add(repository);
                }

                StatusMessage = $"Loaded {Repositories.Count} repositories";
                ShowMessage(MessageType.Success, "Success", $"Loaded {Repositories.Count} repositories");
            }
            else
            {
                var errorMsg = $"Error loading repositories: {result.Error}";
                StatusMessage = errorMsg;
                ShowMessage(MessageType.Error, "Error", errorMsg);
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"Error: {ex.Message}";
            StatusMessage = errorMsg;
            ShowMessage(MessageType.Error, "Exception", errorMsg);
        }
    }

    /// <summary>
    /// Loads VAR packages.
    /// </summary>
    private async Task LoadVarPackagesAsync()
    {
        try
        {
            var query = new VarSearchQuery(
                CreatorName: null,
                PackageName: null,
                Version: null,
                LicenseType: null,
                RepositoryId: null,
                IsInstalled: null,
                InstallationTargetId: null,
                SearchText: SearchText,
                CreatedAfter: null,
                CreatedBefore: null,
                MinFileSize: null,
                MaxFileSize: null
            )
            {
                Skip = null,
                Take = 100, // Limit to first 100 for now
                SortBy = "name",
                SortDescending = false
            };

            var result = await _searchService.SearchAsync(query, CancellationToken.None);

            if (result.IsSuccess)
            {
                VarPackages.Clear();
                var pagedResult = result.Value!;
                TotalVarPackages = pagedResult.TotalCount;

                foreach (var varPackage in pagedResult.Items)
                {
                    VarPackages.Add(varPackage);
                }

                StatusMessage = $"Loaded {VarPackages.Count} of {TotalVarPackages} VAR packages";
                ShowMessage(MessageType.Info, "Loaded", $"Loaded {VarPackages.Count} of {TotalVarPackages} VAR packages");
            }
            else
            {
                var errorMsg = $"Error loading VAR packages: {result.Error}";
                StatusMessage = errorMsg;
                ShowMessage(MessageType.Error, "Error", errorMsg);
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"Error: {ex.Message}";
            StatusMessage = errorMsg;
            ShowMessage(MessageType.Error, "Exception", errorMsg);
        }
    }

    /// <summary>
    /// Searches VAR packages based on the current search text.
    /// </summary>
    private async Task SearchAsync()
    {
        await LoadVarPackagesAsync();
    }

    /// <summary>
    /// Installs the selected VAR package.
    /// </summary>
    private async Task InstallSelectedVarPackageAsync()
    {
        if (SelectedVarPackage == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Installing {SelectedVarPackage.VarName}...";

            // Get active installation target
            var activeTargetResult = await _installationTargetService.GetActiveTargetAsync(CancellationToken.None);
            if (!activeTargetResult.IsSuccess || activeTargetResult.Value == null)
            {
                StatusMessage = "No active installation target found. Please set an active target first.";
                ShowMessage(MessageType.Warning, "No Active Target", "Please set an active installation target in Installation Target Management.");
                return;
            }

            var activeTarget = activeTargetResult.Value;

            var command = new Application.Commands.InstallVarPackageCommand(
                VarPackageId: SelectedVarPackage.Id,
                InstallationTargetId: activeTarget.Id,
                InstallDependencies: true,
                SkipIfAlreadyInstalled: true
            );

            var result = await _installationService.InstallVarPackageAsync(command, CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = $"{SelectedVarPackage.VarName} installed successfully";
                ShowMessage(MessageType.Success, "Installed", $"{SelectedVarPackage.VarName} installed successfully");
                
                // Refresh VAR packages to update installation status
                await LoadVarPackagesAsync();
            }
            else
            {
                StatusMessage = $"Installation failed: {result.Error}";
                ShowMessage(MessageType.Error, "Installation Failed", result.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Exception: {ex.Message}";
            ShowMessage(MessageType.Error, "Exception", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Uninstalls the selected VAR package.
    /// </summary>
    private async Task UninstallSelectedVarPackageAsync()
    {
        if (SelectedVarPackage == null || SelectedVarPackage.InstallationStatus == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Uninstalling {SelectedVarPackage.VarName}...";

            var command = new Application.Commands.UninstallVarPackageCommand(
                VarPackageId: SelectedVarPackage.Id,
                InstallationTargetId: SelectedVarPackage.InstallationStatus.InstallationTargetId,
                RemoveSymlink: true
            );

            var result = await _installationService.UninstallVarPackageAsync(command, CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = $"{SelectedVarPackage.VarName} uninstalled successfully";
                ShowMessage(MessageType.Success, "Uninstalled", $"{SelectedVarPackage.VarName} uninstalled successfully");
                
                // Refresh VAR packages to update installation status
                await LoadVarPackagesAsync();
            }
            else
            {
                StatusMessage = $"Uninstallation failed: {result.Error}";
                ShowMessage(MessageType.Error, "Uninstallation Failed", result.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Exception: {ex.Message}";
            ShowMessage(MessageType.Error, "Exception", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Event fired when VAR package details should be shown.
    /// </summary>
    public event EventHandler<VarPackageDto>? ShowVarPackageDetailsRequested;

    /// <summary>
    /// Shows VAR package details dialog.
    /// </summary>
    private void ShowVarPackageDetails()
    {
        if (SelectedVarPackage == null) return;
        
        StatusMessage = $"Opening details for {SelectedVarPackage.VarName}...";
        ShowVarPackageDetailsRequested?.Invoke(this, SelectedVarPackage);
    }

    /// <summary>
    /// Shows a message to the user.
    /// </summary>
    private void ShowMessage(MessageType type, string title, string message)
    {
        CurrentMessage = new MessageViewModel
        {
            Type = type,
            Title = title,
            Message = message,
            Timestamp = DateTime.Now
        };

        // Auto-dismiss after 5 seconds for info/success messages
        if (type == MessageType.Info || type == MessageType.Success)
        {
            _ = Task.Delay(5000).ContinueWith(_ =>
            {
                if (CurrentMessage?.Timestamp == CurrentMessage?.Timestamp)
                {
                    CurrentMessage = null;
                }
            });
        }
    }
}

