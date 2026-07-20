using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Sdk.Settings;
using VarVault.Sdk.Threading;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// doc 26 · G-0 — screens must load their data on navigation (fixes the "blank on arrival" defect proven in
/// audit 25 §4, where <c>Navigate</c> only swapped the view and never called <c>LoadAsync</c>).
/// </summary>
[Trait("Category", TestCategories.E2E)]
public class ShellNavigationTests
{
    // G-0.2 · navigation alone populates the screen — no manual LoadAsync/RefreshAsync in the test body.
    [AvaloniaFact]
    public async Task Navigating_to_a_screen_loads_its_data()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Alpha.One.1", copyCount: 1);

        using var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        // Dashboard: navigating loads the summary (was null → HasSummary=false before G-0).
        shell.Navigate("dashboard");
        if (shell.PendingScreenLoad is not null)
            await shell.PendingScreenLoad;
        UiE2E.Pump();
        var dash = (DashboardViewModel)shell.ActiveScreen!;
        Assert.True(dash.HasSummary, "navigating to Dashboard should have loaded its summary");

        // Library: navigating populates the grid — no explicit RefreshAsync (contrast the old shell).
        shell.Navigate("library");
        if (shell.PendingScreenLoad is not null)
            await shell.PendingScreenLoad;
        UiE2E.Pump();
        var lib = (LibraryViewModel)shell.ActiveScreen!;
        Assert.Single(lib.Items);
    }

    // G-0.3 · when the background index job finishes, the active screen reloads once.
    [Fact]
    public async Task Active_screen_reloads_when_indexing_completes()
    {
        var screen = new CountingScreen();
        var queue = new FakeJobQueue();
        var shell = new ShellViewModel(
            new Dictionary<string, object> { ["x"] = screen }, initial: "x", jobQueue: queue);

        Assert.Equal(1, screen.LoadCount); // loaded once on the ctor's Navigate (G-0.2)

        queue.SetActive(new JobHandle("Indexing library"));
        await shell.RefreshLiveStateAsync();
        Assert.Equal(1, screen.LoadCount); // still indexing → no reload

        queue.SetActive(); // indexing finished
        await shell.RefreshLiveStateAsync();
        Assert.Equal(2, screen.LoadCount); // reloaded exactly once (G-0.3)

        await shell.RefreshLiveStateAsync();
        Assert.Equal(2, screen.LoadCount); // idle → no repeat reloads
    }

    // G-0.4 · Save before Load must not overwrite stored settings with blanks.
    [Fact]
    public async Task Settings_save_before_load_does_not_wipe_stored_settings()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        await settings.SetAsync(SettingKeys.VamPath, @"F:\VaM");

        var vm = new SettingsViewModel(settings);
        await vm.SaveAsync(); // no Load first → must be a no-op
        Assert.Equal(@"F:\VaM", await settings.GetAsync(SettingKeys.VamPath));

        await vm.LoadAsync();
        vm.VamPath = @"G:\VaM2";
        await vm.SaveAsync(); // now a real edit persists
        Assert.Equal(@"G:\VaM2", await settings.GetAsync(SettingKeys.VamPath));
    }

    private sealed class CountingScreen : ILoadableScreen
    {
        public int LoadCount { get; private set; }
        public Task LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeJobQueue : IJobQueue
    {
        private IReadOnlyList<JobHandle> _active = [];
        public void SetActive(params JobHandle[] jobs) => _active = jobs;
        public IReadOnlyList<JobHandle> Active => _active;
        public JobHandle Enqueue(string name, Func<JobContext, Task> work) => new(name);
    }
}
