namespace VarVault.App.Services;

/// <summary>App clipboard abstraction for copy-to-clipboard actions in dialogs.</summary>
public interface IClipboard
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>Avalonia clipboard implementation.</summary>
public sealed class AvaloniaClipboard : IClipboard
{
    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        var top = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (top?.Clipboard is { } clip)
            await clip.SetTextAsync(text).ConfigureAwait(false);
    }
}
