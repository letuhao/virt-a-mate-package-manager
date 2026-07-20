namespace VarVault.App.ViewModels;

/// <summary>
/// Shown (in <c>StartupErrorWindow</c>) instead of a blank window when startup composition throws — the user gets a
/// copyable message + detail rather than dead chrome bound to a null DataContext. (24-checklist C1.)
/// </summary>
public sealed class StartupErrorViewModel(string message, string detail)
{
    public string Message { get; } = message;
    public string Detail { get; } = detail;
}
