using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VirtaMatePackageManager.Presentation.Services;
using VirtaMatePackageManager.Presentation.ViewModels;

namespace VirtaMatePackageManager.Presentation;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel? _viewModel;

    /// <summary>
    /// Constructor for dependency injection.
    /// </summary>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        
        // Set DataContext to ViewModel
        DataContext = _viewModel;
        
        // Set window title
        Title = "Virt-a-Mate Package Manager";
        
        // Subscribe to ViewModel events
        _viewModel.ShowVarPackageDetailsRequested += OnShowVarPackageDetailsRequested;
        
        // Initialize ViewModel when window is loaded
        Loaded += MainWindow_Loaded;
    }

    /// <summary>
    /// Parameterless constructor for XAML designer.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Initialize ViewModel and load data
        if (_viewModel != null)
        {
            await _viewModel.InitializeAsync();
        }
    }

    private void MenuItemExit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MenuItemManageRepositories_Click(object sender, RoutedEventArgs e)
    {
        // Open repository management dialog
        var viewModel = ServiceProviderFactory.ServiceProvider.GetRequiredService<ViewModels.RepositoryManagementViewModel>();
        var dialog = new Dialogs.RepositoryManagementDialog(viewModel)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void MenuItemManageInstallationTargets_Click(object sender, RoutedEventArgs e)
    {
        // Open installation target management dialog
        var viewModel = ServiceProviderFactory.ServiceProvider.GetRequiredService<ViewModels.InstallationTargetManagementViewModel>();
        var dialog = new Dialogs.InstallationTargetManagementDialog(viewModel)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private async void MenuItemScanRepositories_Click(object sender, RoutedEventArgs e)
    {
        await ScanAllRepositoriesAsync();
    }

    private async Task ScanAllRepositoriesAsync()
    {
        try
        {
            var viewModel = ServiceProviderFactory.ServiceProvider.GetRequiredService<ViewModels.ScanProgressViewModel>();
            var dialog = new Dialogs.ScanProgressDialog(viewModel)
            {
                Owner = this
            };
            
            // Start scanning in background (non-blocking for dialog)
            _ = dialog.ScanAllRepositoriesAsync();
            
            dialog.ShowDialog();
            
            // Refresh VAR packages after scan completes
            if (_viewModel != null)
            {
                _viewModel.RefreshVarPackagesCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to start repository scan: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void MenuItemBackendSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var viewModel = ServiceProviderFactory.ServiceProvider.GetRequiredService<ViewModels.BackendSettingsViewModel>();
            var dialog = new Dialogs.BackendSettingsDialog(viewModel)
            {
                Owner = this
            };
            
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to open backend settings: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void MenuItemAbout_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show(
            "Virt-a-Mate Package Manager\nVersion 1.0.0\n\nA modern package management system for Virt-a-Mate.",
            "About",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void DataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel?.SelectedVarPackage != null)
        {
            // Open VAR package details dialog
            await OpenVarPackageDetailsDialogAsync(_viewModel.SelectedVarPackage.Id);
        }
    }

    private async void OnShowVarPackageDetailsRequested(object? sender, Application.DTOs.VarPackageDto varPackage)
    {
        await OpenVarPackageDetailsDialogAsync(varPackage.Id);
    }

    private async Task OpenVarPackageDetailsDialogAsync(int varPackageId)
    {
        try
        {
            var viewModel = ServiceProviderFactory.ServiceProvider.GetRequiredService<ViewModels.VarPackageDetailsViewModel>();
            var dialog = new Dialogs.VarPackageDetailsDialog(viewModel)
            {
                Owner = this
            };
            
            await dialog.InitializeAsync(varPackageId);
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to open VAR package details: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}