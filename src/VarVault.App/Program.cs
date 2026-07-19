using Avalonia;

namespace VarVault.App;

internal static class Program
{
    // Avalonia entry point. Keep initialization here minimal and before any UI is created.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
