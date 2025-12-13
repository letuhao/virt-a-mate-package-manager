using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Application.Services;
using VirtaMatePackageManager.Presentation.ViewModels.Messaging;

namespace VirtaMatePackageManager.Presentation.ViewModels;

/// <summary>
/// ViewModel for repository scanning progress dialog.
/// </summary>
public class ScanProgressViewModel : ViewModelBase
{
    private readonly IRepositoryService _repositoryService;
    private CancellationTokenSource? _cancellationTokenSource;
    
    private string _statusMessage = "Initializing...";
    private string _currentFile = string.Empty;
    private string _repositoryName = string.Empty;
    private double _progressValue;
    private int _filesScanned;
    private int _filesAdded;
    private int _filesUpdated;
    private int _filesDeleted;
    private int _errorsCount;
    private bool _isScanning;
    private bool _isCompleted;
    private ScanResultDto? _scanResult;

    public ScanProgressViewModel(IRepositoryService repositoryService)
    {
        _repositoryService = repositoryService ?? throw new ArgumentNullException(nameof(repositoryService));
        CancelCommand = new RelayCommand(_ => CancelScan(), _ => IsScanning && !IsCompleted);
    }

    /// <summary>
    /// Current status message.
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// Current file being processed.
    /// </summary>
    public string CurrentFile
    {
        get => _currentFile;
        set => SetProperty(ref _currentFile, value);
    }

    /// <summary>
    /// Repository name being scanned.
    /// </summary>
    public string RepositoryName
    {
        get => _repositoryName;
        set => SetProperty(ref _repositoryName, value);
    }

    /// <summary>
    /// Progress value (0-100).
    /// </summary>
    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    /// <summary>
    /// Number of files scanned.
    /// </summary>
    public int FilesScanned
    {
        get => _filesScanned;
        set => SetProperty(ref _filesScanned, value);
    }

    /// <summary>
    /// Number of files added.
    /// </summary>
    public int FilesAdded
    {
        get => _filesAdded;
        set => SetProperty(ref _filesAdded, value);
    }

    /// <summary>
    /// Number of files updated.
    /// </summary>
    public int FilesUpdated
    {
        get => _filesUpdated;
        set => SetProperty(ref _filesUpdated, value);
    }

    /// <summary>
    /// Number of files deleted.
    /// </summary>
    public int FilesDeleted
    {
        get => _filesDeleted;
        set => SetProperty(ref _filesDeleted, value);
    }

    /// <summary>
    /// Number of errors encountered.
    /// </summary>
    public int ErrorsCount
    {
        get => _errorsCount;
        set => SetProperty(ref _errorsCount, value);
    }

    /// <summary>
    /// Indicates whether scanning is in progress.
    /// </summary>
    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetProperty(ref _isScanning, value))
            {
                ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Indicates whether scanning is completed.
    /// </summary>
    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (SetProperty(ref _isCompleted, value))
            {
                ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Scan result after completion.
    /// </summary>
    public ScanResultDto? ScanResult
    {
        get => _scanResult;
        set => SetProperty(ref _scanResult, value);
    }

    /// <summary>
    /// Command to cancel the scan.
    /// </summary>
    public ICommand CancelCommand { get; }

    /// <summary>
    /// Starts scanning a single repository.
    /// </summary>
    public async Task ScanRepositoryAsync(int repositoryId, string repositoryName)
    {
        RepositoryName = repositoryName;
        IsScanning = true;
        IsCompleted = false;
        _cancellationTokenSource = new CancellationTokenSource();

        // Create progress reporter
        var progress = new Progress<ScanProgressInfo>(OnProgressUpdate);

        try
        {
            StatusMessage = $"Scanning repository: {repositoryName}...";
            CurrentFile = string.Empty;
            ProgressValue = 0;
            ResetStatistics();

            var result = await _repositoryService.ScanRepositoryAsync(repositoryId, progress, _cancellationTokenSource.Token);

            if (result.IsSuccess && result.Value != null)
            {
                UpdateFromScanResult(result.Value);
                StatusMessage = "Scan completed successfully";
            }
            else
            {
                StatusMessage = $"Scan failed: {result.Error}";
                ErrorsCount++;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled by user";
            ProgressValue = 0;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
            ErrorsCount++;
        }
        finally
        {
            IsScanning = false;
            IsCompleted = true;
        }
    }

    /// <summary>
    /// Handles progress updates from the scan operation.
    /// </summary>
    private void OnProgressUpdate(ScanProgressInfo progressInfo)
    {
        // Update UI on the UI thread
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            FilesScanned = progressInfo.FilesScanned;
            FilesAdded = progressInfo.FilesAdded;
            FilesUpdated = progressInfo.FilesUpdated;
            FilesDeleted = progressInfo.FilesDeleted;
            ErrorsCount = progressInfo.ErrorsCount;
            ProgressValue = progressInfo.ProgressPercentage;
            
            if (!string.IsNullOrEmpty(progressInfo.CurrentFile))
            {
                CurrentFile = progressInfo.CurrentFile;
            }

            // Update status message with progress
            if (progressInfo.TotalFiles > 0)
            {
                StatusMessage = $"Scanning: {progressInfo.FilesScanned} / {progressInfo.TotalFiles} files";
            }
            else if (!string.IsNullOrEmpty(progressInfo.CurrentFile))
            {
                StatusMessage = $"Scanning: {progressInfo.CurrentFile}";
            }
        });
    }

    /// <summary>
    /// Resets all statistics.
    /// </summary>
    private void ResetStatistics()
    {
        FilesScanned = 0;
        FilesAdded = 0;
        FilesUpdated = 0;
        FilesDeleted = 0;
        ErrorsCount = 0;
        ProgressValue = 0;
        CurrentFile = string.Empty;
    }

    /// <summary>
    /// Starts scanning all enabled repositories.
    /// </summary>
    public async Task ScanAllRepositoriesAsync()
    {
        RepositoryName = "All Enabled Repositories";
        IsScanning = true;
        IsCompleted = false;
        _cancellationTokenSource = new CancellationTokenSource();

        // Create progress reporter
        var progress = new Progress<ScanProgressInfo>(OnProgressUpdate);

        try
        {
            StatusMessage = "Scanning all enabled repositories...";
            CurrentFile = string.Empty;
            ProgressValue = 0;
            ResetStatistics();

            var result = await _repositoryService.ScanAllRepositoriesAsync(progress, _cancellationTokenSource.Token);

            if (result.IsSuccess && result.Value != null)
            {
                UpdateFromScanResult(result.Value);
                StatusMessage = "All repositories scanned successfully";
            }
            else
            {
                StatusMessage = $"Scan failed: {result.Error}";
                ErrorsCount++;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled by user";
            ProgressValue = 0;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
            ErrorsCount++;
        }
        finally
        {
            IsScanning = false;
            IsCompleted = true;
        }
    }

    /// <summary>
    /// Cancels the current scan operation.
    /// </summary>
    private void CancelScan()
    {
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
        {
            StatusMessage = "Cancelling scan...";
            _cancellationTokenSource.Cancel();
        }
    }

    /// <summary>
    /// Updates the view model from scan result.
    /// </summary>
    private void UpdateFromScanResult(ScanResultDto result)
    {
        ScanResult = result;
        FilesScanned = result.FilesScanned;
        FilesAdded = result.FilesAdded;
        FilesUpdated = result.FilesUpdated;
        FilesDeleted = result.FilesDeleted;
        ErrorsCount = result.ErrorsCount;
        
        // Calculate progress based on status
        if (result.Status == ScanStatus.Completed)
        {
            ProgressValue = 100;
        }
        else if (result.Status == ScanStatus.Cancelled)
        {
            ProgressValue = 0;
        }
    }

    /// <summary>
    /// Cleans up resources.
    /// </summary>
    public new void Dispose()
    {
        _cancellationTokenSource?.Dispose();
    }
}

