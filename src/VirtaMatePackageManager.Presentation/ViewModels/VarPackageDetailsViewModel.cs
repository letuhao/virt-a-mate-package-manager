using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using VirtaMatePackageManager.Application.Commands;
using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Application.Services;
using VirtaMatePackageManager.Presentation.ViewModels.Messaging;

namespace VirtaMatePackageManager.Presentation.ViewModels;

/// <summary>
/// ViewModel for VAR package details dialog.
/// </summary>
public class VarPackageDetailsViewModel : ViewModelBase
{
    private readonly IVarPackageService _varPackageService;
    private readonly IInstallationService _installationService;
    private readonly IInstallationTargetService _installationTargetService;
    private readonly IDependencyService _dependencyService;
    
    private VarPackageDto? _varPackage;
    private VarPackageMetadata? _metadata;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private bool _isInstalled;
    private InstallationStatusDto? _installationStatus;

    public VarPackageDetailsViewModel(
        IVarPackageService varPackageService,
        IInstallationService installationService,
        IInstallationTargetService installationTargetService,
        IDependencyService dependencyService)
    {
        _varPackageService = varPackageService ?? throw new ArgumentNullException(nameof(varPackageService));
        _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
        _installationTargetService = installationTargetService ?? throw new ArgumentNullException(nameof(installationTargetService));
        _dependencyService = dependencyService ?? throw new ArgumentNullException(nameof(dependencyService));

        Dependencies = new ObservableCollection<DependencyDto>();
        ReverseDependencies = new ObservableCollection<VarPackageDto>();

        InstallCommand = new RelayCommand(async _ => await InstallAsync(), _ => !IsLoading && !IsInstalled);
        UninstallCommand = new RelayCommand(async _ => await UninstallAsync(), _ => !IsLoading && IsInstalled);
        ExtractPreviewCommand = new RelayCommand(async _ => await ExtractPreviewAsync(), _ => !IsLoading && VarPackage != null);
        RefreshCommand = new RelayCommand(async _ => await LoadDetailsAsync(), _ => !IsLoading && VarPackage != null);
    }

    /// <summary>
    /// The VAR package being displayed.
    /// </summary>
    public VarPackageDto? VarPackage
    {
        get => _varPackage;
        set
        {
            if (SetProperty(ref _varPackage, value))
            {
                if (value != null)
                {
                    IsInstalled = value.InstallationStatus != null;
                    InstallationStatus = value.InstallationStatus;
                    _ = LoadDetailsAsync();
                }
                ((RelayCommand)ExtractPreviewCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Extended metadata for the VAR package.
    /// </summary>
    public VarPackageMetadata? Metadata
    {
        get => _metadata;
        set => SetProperty(ref _metadata, value);
    }

    /// <summary>
    /// Collection of dependencies.
    /// </summary>
    public ObservableCollection<DependencyDto> Dependencies { get; }

    /// <summary>
    /// Collection of VAR packages that depend on this one (reverse dependencies).
    /// </summary>
    public ObservableCollection<VarPackageDto> ReverseDependencies { get; }

    /// <summary>
    /// Indicates whether the VAR package is installed.
    /// </summary>
    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (SetProperty(ref _isInstalled, value))
            {
                ((RelayCommand)InstallCommand).RaiseCanExecuteChanged();
                ((RelayCommand)UninstallCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Installation status information.
    /// </summary>
    public InstallationStatusDto? InstallationStatus
    {
        get => _installationStatus;
        set => SetProperty(ref _installationStatus, value);
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
                ((RelayCommand)InstallCommand).RaiseCanExecuteChanged();
                ((RelayCommand)UninstallCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ExtractPreviewCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
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
    /// Command to install the VAR package.
    /// </summary>
    public ICommand InstallCommand { get; }

    /// <summary>
    /// Command to uninstall the VAR package.
    /// </summary>
    public ICommand UninstallCommand { get; }

    /// <summary>
    /// Command to extract preview images.
    /// </summary>
    public ICommand ExtractPreviewCommand { get; }

    /// <summary>
    /// Command to refresh VAR package details.
    /// </summary>
    public ICommand RefreshCommand { get; }

    /// <summary>
    /// Loads VAR package details.
    /// </summary>
    public async Task InitializeAsync(int varPackageId)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading VAR package details...";

            var result = await _varPackageService.GetVarPackageByIdAsync(varPackageId, CancellationToken.None);
            if (result.IsSuccess && result.Value != null)
            {
                VarPackage = result.Value;
                IsInstalled = VarPackage.InstallationStatus != null;
                InstallationStatus = VarPackage.InstallationStatus;

                // Load extended metadata
                await LoadMetadataAsync();
                
                // Load dependencies
                await LoadDependenciesAsync();
                
                // Load reverse dependencies
                await LoadReverseDependenciesAsync();

                StatusMessage = "Details loaded successfully";
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
    /// Loads VAR package details (when VarPackage is already set).
    /// </summary>
    private async Task LoadDetailsAsync()
    {
        if (VarPackage == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Refreshing details...";

            var warnings = new List<string>();

            // Load all details in parallel and collect warnings
            var metadataResult = await LoadMetadataAsync();
            if (!string.IsNullOrEmpty(metadataResult))
            {
                warnings.Add($"Metadata: {metadataResult}");
            }

            var dependenciesResult = await LoadDependenciesAsync();
            if (!string.IsNullOrEmpty(dependenciesResult))
            {
                warnings.Add($"Dependencies: {dependenciesResult}");
            }

            var reverseDependenciesResult = await LoadReverseDependenciesAsync();
            if (!string.IsNullOrEmpty(reverseDependenciesResult))
            {
                warnings.Add($"Reverse dependencies: {reverseDependenciesResult}");
            }

            // Show success or warnings
            if (warnings.Count == 0)
            {
                StatusMessage = "Details refreshed";
            }
            else
            {
                StatusMessage = $"Details refreshed with {warnings.Count} warning(s): {string.Join("; ", warnings)}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading details: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads extended metadata.
    /// Returns error message if failed, null if successful.
    /// </summary>
    private async Task<string?> LoadMetadataAsync()
    {
        if (VarPackage == null) return null;

        try
        {
            var result = await _varPackageService.ExtractMetadataAsync(VarPackage.FilePath, CancellationToken.None);
            if (result.IsSuccess && result.Value != null)
            {
                Metadata = result.Value;
                return null; // Success
            }
            else
            {
                // Return error message
                return result.Error ?? "Failed to extract metadata";
            }
        }
        catch (Exception ex)
        {
            // Log and return error message
            System.Diagnostics.Debug.WriteLine($"Failed to load metadata: {ex.Message}");
            return ex.Message;
        }
    }

    /// <summary>
    /// Loads dependencies.
    /// Returns error message if failed, null if successful.
    /// </summary>
    private async Task<string?> LoadDependenciesAsync()
    {
        if (VarPackage == null) return null;

        try
        {
            var result = await _dependencyService.GetDependenciesAsync(VarPackage.Id, CancellationToken.None);
            if (result.IsSuccess)
            {
                Dependencies.Clear();
                foreach (var dependency in result.Value!)
                {
                    Dependencies.Add(dependency);
                }
                return null; // Success
            }
            else
            {
                // Return error message
                return result.Error ?? "Failed to load dependencies";
            }
        }
        catch (Exception ex)
        {
            // Log and return error message
            System.Diagnostics.Debug.WriteLine($"Failed to load dependencies: {ex.Message}");
            return ex.Message;
        }
    }

    /// <summary>
    /// Loads reverse dependencies.
    /// Returns error message if failed, null if successful.
    /// </summary>
    private async Task<string?> LoadReverseDependenciesAsync()
    {
        if (VarPackage == null) return null;

        try
        {
            var result = await _dependencyService.GetReverseDependenciesAsync(VarPackage.Id, CancellationToken.None);
            if (result.IsSuccess)
            {
                ReverseDependencies.Clear();
                foreach (var varPackage in result.Value!)
                {
                    ReverseDependencies.Add(varPackage);
                }
                return null; // Success
            }
            else
            {
                // Return error message
                return result.Error ?? "Failed to load reverse dependencies";
            }
        }
        catch (Exception ex)
        {
            // Log and return error message
            System.Diagnostics.Debug.WriteLine($"Failed to load reverse dependencies: {ex.Message}");
            return ex.Message;
        }
    }

    /// <summary>
    /// Installs the VAR package.
    /// </summary>
    private async Task InstallAsync()
    {
        if (VarPackage == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Installing {VarPackage.VarName}...";

            // Get active installation target
            var activeTargetResult = await _installationTargetService.GetActiveTargetAsync(CancellationToken.None);
            if (!activeTargetResult.IsSuccess || activeTargetResult.Value == null)
            {
                StatusMessage = "No active installation target found";
                return;
            }

            var activeTarget = activeTargetResult.Value;

            var command = new InstallVarPackageCommand(
                VarPackageId: VarPackage.Id,
                InstallationTargetId: activeTarget.Id,
                InstallDependencies: true,
                SkipIfAlreadyInstalled: true
            );

            var result = await _installationService.InstallVarPackageAsync(command, CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = $"Installed successfully";
                IsInstalled = true;
                
                // Refresh VAR package to get updated installation status
                var refreshResult = await _varPackageService.GetVarPackageByIdAsync(VarPackage.Id, CancellationToken.None);
                if (refreshResult.IsSuccess && refreshResult.Value != null)
                {
                    VarPackage = refreshResult.Value;
                    InstallationStatus = VarPackage.InstallationStatus;
                }
            }
            else
            {
                StatusMessage = $"Installation failed: {result.Error}";
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
    /// Uninstalls the VAR package.
    /// </summary>
    private async Task UninstallAsync()
    {
        if (VarPackage == null || InstallationStatus == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Uninstalling {VarPackage.VarName}...";

            var command = new UninstallVarPackageCommand(
                VarPackageId: VarPackage.Id,
                InstallationTargetId: InstallationStatus.InstallationTargetId,
                RemoveSymlink: true
            );

            var result = await _installationService.UninstallVarPackageAsync(command, CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = $"Uninstalled successfully";
                IsInstalled = false;
                InstallationStatus = null;
                
                // Refresh VAR package
                var refreshResult = await _varPackageService.GetVarPackageByIdAsync(VarPackage.Id, CancellationToken.None);
                if (refreshResult.IsSuccess && refreshResult.Value != null)
                {
                    VarPackage = refreshResult.Value;
                    InstallationStatus = VarPackage.InstallationStatus;
                }
            }
            else
            {
                StatusMessage = $"Uninstallation failed: {result.Error}";
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
    /// Extracts preview images for the VAR package.
    /// </summary>
    private async Task ExtractPreviewAsync()
    {
        if (VarPackage == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Extracting preview images...";

            var result = await _varPackageService.ExtractPreviewImageAsync(VarPackage.Id, CancellationToken.None);

            if (result.IsSuccess)
            {
                StatusMessage = $"Preview extracted: {result.Value}";
                
                // Refresh VAR package to get updated preview path
                var refreshResult = await _varPackageService.GetVarPackageByIdAsync(VarPackage.Id, CancellationToken.None);
                if (refreshResult.IsSuccess && refreshResult.Value != null)
                {
                    VarPackage = refreshResult.Value;
                }
            }
            else
            {
                StatusMessage = $"Preview extraction failed: {result.Error}";
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
}

