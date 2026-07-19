using CommunityToolkit.Mvvm.ComponentModel;

namespace VarVault.App.ViewModels;

/// <summary>Root view-model for the shell window; hosts the library view. (Slice-1 UI.)</summary>
public sealed partial class MainWindowViewModel(LibraryViewModel library) : ObservableObject
{
    public LibraryViewModel Library { get; } = library;

    [ObservableProperty] private string _title = "VarVault";
}
