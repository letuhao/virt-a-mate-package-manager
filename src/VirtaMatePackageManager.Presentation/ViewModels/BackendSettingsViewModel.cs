using System.Windows.Input;
using VirtaMatePackageManager.Application.Services;
using VirtaMatePackageManager.Presentation.ViewModels;

namespace VirtaMatePackageManager.Presentation.ViewModels;

/// <summary>
/// ViewModel for the Backend Settings dialog.
/// </summary>
public class BackendSettingsViewModel : ViewModelBase
{
    private readonly IBackendSettingsService _settingsService;
    private string _connectionString = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isTesting;
    private bool _isConnectionSuccessful;
    private bool _hasChanges;

    public BackendSettingsViewModel(IBackendSettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        
        TestConnectionCommand = new RelayCommand(async _ => await TestConnectionAsync(), _ => !IsTesting && !string.IsNullOrWhiteSpace(ConnectionString));
        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => !IsTesting && HasChanges);
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(), _ => true);
        
        LoadConnectionString();
    }

    public string ConnectionString
    {
        get => _connectionString;
        set
        {
            if (SetProperty(ref _connectionString, value))
            {
                HasChanges = true;
                IsConnectionSuccessful = false;
                StatusMessage = string.Empty;
                ((RelayCommand)TestConnectionCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsTesting
    {
        get => _isTesting;
        set
        {
            if (SetProperty(ref _isTesting, value))
            {
                ((RelayCommand)TestConnectionCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsConnectionSuccessful
    {
        get => _isConnectionSuccessful;
        set => SetProperty(ref _isConnectionSuccessful, value);
    }

    public bool HasChanges
    {
        get => _hasChanges;
        set
        {
            if (SetProperty(ref _hasChanges, value))
            {
                ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand TestConnectionCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public event Action? RequestClose;

    private async void LoadConnectionString()
    {
        try
        {
            var result = await _settingsService.GetConnectionStringAsync(CancellationToken.None);
            if (result.IsSuccess)
            {
                ConnectionString = result.Value;
                HasChanges = false;
            }
            else
            {
                StatusMessage = $"Failed to load connection string: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading connection string: {ex.Message}";
        }
    }

    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            StatusMessage = "Connection string cannot be empty.";
            IsConnectionSuccessful = false;
            return;
        }

        IsTesting = true;
        StatusMessage = "Testing connection...";
        IsConnectionSuccessful = false;

        try
        {
            var result = await _settingsService.TestConnectionAsync(ConnectionString, CancellationToken.None);
            
            if (result.IsSuccess)
            {
                StatusMessage = "✓ Connection successful!";
                IsConnectionSuccessful = true;
            }
            else
            {
                StatusMessage = $"✗ Connection failed: {result.Error}";
                IsConnectionSuccessful = false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error testing connection: {ex.Message}";
            IsConnectionSuccessful = false;
        }
        finally
        {
            IsTesting = false;
        }
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            StatusMessage = "Connection string cannot be empty.";
            return;
        }

        // Test connection before saving
        IsTesting = true;
        StatusMessage = "Testing connection before saving...";

        try
        {
            var testResult = await _settingsService.TestConnectionAsync(ConnectionString, CancellationToken.None);
            
            if (!testResult.IsSuccess)
            {
                StatusMessage = $"Cannot save: Connection test failed. {testResult.Error}";
                IsConnectionSuccessful = false;
                IsTesting = false;
                return;
            }

            // Save connection string
            StatusMessage = "Saving connection string...";
            var saveResult = await _settingsService.SaveConnectionStringAsync(ConnectionString, CancellationToken.None);
            
            if (saveResult.IsSuccess)
            {
                StatusMessage = "✓ Settings saved successfully! Please restart the application for changes to take effect.";
                HasChanges = false;
                IsConnectionSuccessful = true;
                
                // Optionally trigger close after a delay
                await Task.Delay(2000);
                RequestClose?.Invoke();
            }
            else
            {
                StatusMessage = $"✗ Failed to save: {saveResult.Error}";
                IsConnectionSuccessful = false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error saving settings: {ex.Message}";
            IsConnectionSuccessful = false;
        }
        finally
        {
            IsTesting = false;
        }
    }
}

