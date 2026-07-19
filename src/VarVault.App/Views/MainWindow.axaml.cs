using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

public partial class MainWindow : Window
{
    private DispatcherTimer? _pollTimer;

    public MainWindow() => InitializeComponent();

    /// <summary>
    /// Start a light poll timer once shown: pulls live jobs/badges/log-dock from the shell's injected
    /// sources so the panel, rail badges, and log dock stay current. (GA-2/GA-3/GA-4.)
    /// </summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _pollTimer.Tick += async (_, _) =>
        {
            if (DataContext is ShellViewModel shell)
                await shell.RefreshLiveStateAsync().ConfigureAwait(true);
        };
        _pollTimer.Start();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _pollTimer?.Stop();
        _pollTimer = null;
        base.OnUnloaded(e);
    }
}
