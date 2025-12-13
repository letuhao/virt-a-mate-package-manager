using System.Windows;
using VirtaMatePackageManager.Presentation.ViewModels;

namespace VirtaMatePackageManager.Presentation.Dialogs;

/// <summary>
/// Interaction logic for RepositoryManagementDialog.xaml
/// </summary>
public partial class RepositoryManagementDialog : Window
{
    private readonly RepositoryManagementViewModel? _viewModel;

    /// <summary>
    /// Constructor with ViewModel injection.
    /// </summary>
    public RepositoryManagementDialog(RepositoryManagementViewModel viewModel)
    {
        InitializeComponent();
        
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        
        Loaded += RepositoryManagementDialog_Loaded;
    }

    /// <summary>
    /// Parameterless constructor for designer.
    /// </summary>
    public RepositoryManagementDialog()
    {
        InitializeComponent();
    }

    private async void RepositoryManagementDialog_Loaded(object sender, RoutedEventArgs e)
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

