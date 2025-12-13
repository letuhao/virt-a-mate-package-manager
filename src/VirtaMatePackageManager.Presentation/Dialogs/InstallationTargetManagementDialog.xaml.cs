using System.Windows;
using VirtaMatePackageManager.Presentation.ViewModels;

namespace VirtaMatePackageManager.Presentation.Dialogs;

/// <summary>
/// Interaction logic for InstallationTargetManagementDialog.xaml
/// </summary>
public partial class InstallationTargetManagementDialog : Window
{
    private readonly InstallationTargetManagementViewModel? _viewModel;

    /// <summary>
    /// Constructor with ViewModel injection.
    /// </summary>
    public InstallationTargetManagementDialog(InstallationTargetManagementViewModel viewModel)
    {
        InitializeComponent();
        
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        
        Loaded += InstallationTargetManagementDialog_Loaded;
    }

    /// <summary>
    /// Parameterless constructor for designer.
    /// </summary>
    public InstallationTargetManagementDialog()
    {
        InitializeComponent();
    }

    private async void InstallationTargetManagementDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.InitializeAsync();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

