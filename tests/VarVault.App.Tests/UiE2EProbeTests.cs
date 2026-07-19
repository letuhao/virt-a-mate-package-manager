using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.Views;
using VarVault.TestKit;
using Avalonia.Headless.XUnit;

namespace VarVault.App.Tests;

/// <summary>Confirms the UI-E2E harness truly renders pixels so screenshots are real evidence.</summary>
public class UiE2EProbeTests
{
    [AvaloniaFact]
    public async Task Real_shell_window_renders_pixels_and_screenshots()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);

        var window = new MainWindow { DataContext = shell };
        window.Show();
        UiE2E.Pump();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame); // Skia headless produced a real bitmap
        Assert.True(frame!.PixelSize.Width > 100 && frame.PixelSize.Height > 100);

        var path = UiE2E.Screenshot(window, "probe-shell");
        Assert.True(System.IO.File.Exists(path));
    }
}
