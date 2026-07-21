using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

public partial class MainWindow : Window
{
    private DispatcherTimer? _pollTimer;

    public MainWindow()
    {
        InitializeComponent();
        ModalHost.CloseRequested += (_, _) =>
        {
            if (DataContext is ShellViewModel shell)
            {
                if (shell.Dialogs.CanGoBack)
                    shell.Dialogs.Back();
                else
                    shell.Dialogs.Close();
            }
        };
    }

    /// <summary>
    /// Start a light poll timer once shown: pulls live jobs/badges/log-dock from the shell's injected
    /// sources so the panel, rail badges, and log dock stay current. (GA-2/GA-3/GA-4.)
    /// </summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // C1.2 · first-run: a zero-repo install opens the onboarding wizard once so a newcomer is guided.
        if (DataContext is ShellViewModel s)
            s.MaybeShowOnboarding();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += async (_, _) =>
        {
            if (_polling)
                return;
            _polling = true;
            try
            {
                if (DataContext is ShellViewModel shell)
                    await shell.RefreshLiveStateAsync().ConfigureAwait(true);
            }
            finally
            {
                _polling = false;
            }
        };
        _pollTimer.Start();
    }

    private bool _polling;

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _pollTimer?.Stop();
        _pollTimer = null;
        base.OnUnloaded(e);
    }
}
