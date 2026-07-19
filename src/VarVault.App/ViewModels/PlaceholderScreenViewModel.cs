using CommunityToolkit.Mvvm.ComponentModel;

namespace VarVault.App.ViewModels;

/// <summary>
/// A stand-in for a screen whose full view lands in a later SCR slice, so the shell always resolves with
/// every screen non-null. (16-checklist SH-6.)
/// </summary>
public sealed partial class PlaceholderScreenViewModel(string title) : ObservableObject
{
    public string Title { get; } = title;
}
