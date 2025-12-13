using System;
using System.Threading.Tasks;
using System.Windows;
using VirtaMatePackageManager.Presentation.ViewModels;

namespace VirtaMatePackageManager.Presentation.Dialogs;

/// <summary>
/// Interaction logic for ScanProgressDialog.xaml
/// </summary>
public partial class ScanProgressDialog : Window
{
    private readonly ScanProgressViewModel _viewModel;

    public ScanProgressDialog(ScanProgressViewModel viewModel)
    {
        InitializeComponent();
        
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        
        DataContext = _viewModel;
        
        Title = "Repository Scanning Progress";
        
        // Close dialog when scan completes
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ScanProgressViewModel.IsCompleted) && _viewModel.IsCompleted)
            {
                // Keep dialog open so user can see results
                // Dialog will be closed manually via Close button
            }
        };
    }

    /// <summary>
    /// Starts scanning a single repository.
    /// </summary>
    public async Task ScanRepositoryAsync(int repositoryId, string repositoryName)
    {
        await _viewModel.ScanRepositoryAsync(repositoryId, repositoryName);
    }

    /// <summary>
    /// Starts scanning all enabled repositories.
    /// </summary>
    public async Task ScanAllRepositoriesAsync()
    {
        await _viewModel.ScanAllRepositoriesAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _viewModel.Dispose();
    }
}

