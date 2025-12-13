using VirtaMatePackageManager.Presentation.ViewModels;

namespace VirtaMatePackageManager.Presentation.Dialogs;

/// <summary>
/// Interaction logic for BackendSettingsDialog.xaml
/// </summary>
public partial class BackendSettingsDialog : Window
{
    private readonly BackendSettingsViewModel _viewModel;

    public BackendSettingsDialog(BackendSettingsViewModel viewModel)
    {
        InitializeComponent();
        
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        
        DataContext = _viewModel;
        
        // Subscribe to close request
        _viewModel.RequestClose += (_, __) => Close();
    }
}

