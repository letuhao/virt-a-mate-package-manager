using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Extensions.DependencyInjection;
using VirtaMatePackageManager.Presentation.Services;
using VirtaMatePackageManager.Presentation.ViewModels;

namespace VirtaMatePackageManager.Presentation.Dialogs;

/// <summary>
/// Interaction logic for VarPackageDetailsDialog.xaml
/// </summary>
public partial class VarPackageDetailsDialog : Window
{
    private readonly VarPackageDetailsViewModel? _viewModel;

    public VarPackageDetailsDialog(VarPackageDetailsViewModel viewModel)
    {
        InitializeComponent();
        
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        
        DataContext = _viewModel;
        
        Title = "VAR Package Details";
    }

    public VarPackageDetailsDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the dialog with a VAR package ID.
    /// </summary>
    public async Task InitializeAsync(int varPackageId)
    {
        if (_viewModel != null)
        {
            await _viewModel.InitializeAsync(varPackageId);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (sender is System.Windows.Documents.Hyperlink hyperlink)
        {
            try
            {
                var uri = hyperlink.NavigateUri ?? new Uri(e.Uri.ToString());
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.ToString(),
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to open link: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}

