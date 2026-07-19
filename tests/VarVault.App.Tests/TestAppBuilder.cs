using Avalonia;
using Avalonia.Headless;
using VarVault.App;
using VarVault.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace VarVault.App.Tests;

/// <summary>Headless Avalonia app for UI-E2E tests. (Checklist TO-10.)</summary>
public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia() // real pixels so UI-E2E tests can capture rendered frames as evidence
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
