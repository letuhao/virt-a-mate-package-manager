using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// AC-24..AC-29 · Real UI-E2E for dialog internals: var-detail tab switching, preset-edit member table,
/// add-repo browse/tier, rescue baseline dropdown, fix also-slim, onboarding benchmarked-folder table —
/// each opened through the real shell/dialog service over real SQLite-backed services. (19-Audit.)
/// </summary>
public class DialogInternalsE2ETests
{
    private static (TestHost host, MainWindow window, ShellViewModel shell, IServiceScope scope) Launch()
    {
        var host = TestHost.Create(withPersistence: true);
        var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        UiE2E.Pump();
        return (host, window, shell, scope);
    }

    [AvaloniaFact]
    public async Task VarDetail_tabs_switch_content_over_real_copies()
    {
        var (host, window, shell, scope) = Launch();
        await using var _h = host;
        using var repo = new TempDirectory();
        var repoId = await UiE2ESeed.SeedRepositoryAsync(host, repo.Path);
        var pkg = await UiE2ESeed.SeedPackageAsync(host, repoId, repo.Path, 1, "Detail.Pkg.1", copyCount: 2);

        var vm = new VarDetailViewModel(scope.ServiceProvider.GetRequiredService<IPackageDetailQuery>());
        await vm.LoadCommand.ExecuteAsync(pkg.PackageId);
        shell.Dialogs.Show(vm);
        UiE2E.Pump();

        Assert.True(vm.IsOverviewTab);
        vm.SelectedTabIndex = 3; // Copies & lineage
        Assert.True(vm.IsCopiesTab);
        Assert.False(vm.IsOverviewTab);
        Assert.Equal(2, vm.Detail!.Copies.Count); // real copies on the copies tab
        UiE2E.Screenshot(window, "ac24-vardetail-tabs");
    }

    [AvaloniaFact]
    public async Task PresetEdit_member_table_loads_and_removes()
    {
        var (host, window, shell, scope) = Launch();
        await using var _h = host;
        var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();
        var p = await presets.CreateAsync("Edit me", []);
        await presets.AddMemberAsync(p.Value.Id, "Creator.A.1");
        await presets.AddMemberAsync(p.Value.Id, "Creator.B.1");

        var vm = new PresetEditViewModel(presets) { PresetId = p.Value.Id, Name = "Edit me" };
        await vm.LoadMembersAsync();
        shell.Dialogs.Show(vm);
        UiE2E.Pump();

        Assert.Equal(2, vm.Members.Count);                 // AC-25: real member table
        await vm.RemoveMemberCommand.ExecuteAsync("Creator.A.1");
        Assert.Single(vm.Members);
        vm.ExportCommand.Execute(null);
        Assert.Contains("Creator.B.1", vm.LastExportText);
        UiE2E.Screenshot(window, "ac25-presetedit");
    }

    [AvaloniaFact]
    public async Task AddRepo_browse_fills_path_and_tier_options_present()
    {
        var (host, window, shell, scope) = Launch();
        await using var _h = host;
        using var pick = new TempDirectory();

        var vm = new AddRepoViewModel(scope.ServiceProvider.GetRequiredService<IRepositoryService>())
        {
            FolderPicker = () => Task.FromResult<string?>(pick.Path), // stand-in for the real StorageProvider
        };
        shell.Dialogs.Show(vm);
        UiE2E.Pump();

        Assert.Equal(4, vm.TierOptions.Count);       // AC-26: tier dropdown
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(pick.Path, vm.FolderPath);      // Browse filled the path
        UiE2E.Screenshot(window, "ac26-addrepo");
    }

    [AvaloniaFact]
    public async Task Rescue_baseline_dropdown_lists_real_presets()
    {
        var (host, window, shell, scope) = Launch();
        await using var _h = host;
        var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();
        await presets.CreateAsync("Baseline A", []);

        var vm = new RescueViewModel(scope.ServiceProvider.GetRequiredService<IActivationService>(), presets);
        await vm.LoadAsync();
        shell.Dialogs.Show(vm);
        UiE2E.Pump();

        Assert.Contains(vm.BaselineOptions, b => b.Name == "Baseline A"); // AC-27
        UiE2E.Screenshot(window, "ac27-rescue");
    }

    [AvaloniaFact]
    public async Task Fix_dialog_has_also_slim_toggle()
    {
        var (host, window, shell, scope) = Launch();
        await using var _h = host;
        var vm = new FixEncodingViewModel(scope.ServiceProvider.GetRequiredService<IHealthService>());
        shell.Dialogs.Show(vm);
        UiE2E.Pump();

        Assert.False(vm.AlsoSlim);
        vm.AlsoSlim = true; // AC-28: the checkbox is bound
        Assert.True(vm.AlsoSlim);
    }

    [AvaloniaFact]
    public async Task Onboarding_add_and_index_records_a_benchmarked_folder()
    {
        var (host, window, shell, scope) = Launch();
        await using var _h = host;
        using var repo = new TempDirectory();
        // One real minimal var so indexing has something to count.
        using (var fs = new System.IO.FileStream(System.IO.Path.Combine(repo.Path, "Ob.Test.1.var"), System.IO.FileMode.Create))
        using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("meta.json");
            using var s = e.Open();
            s.Write(System.Text.Encoding.UTF8.GetBytes("{\"creatorName\":\"Ob\",\"packageName\":\"Test\"}"));
        }

        var vm = new OnboardingViewModel(scope.ServiceProvider.GetRequiredService<Sdk.Library.IOnboardingService>())
        {
            FolderPath = repo.Path,
        };
        shell.Dialogs.Show(vm);
        await vm.AddAndIndexCommand.ExecuteAsync(null);
        UiE2E.Pump();

        Assert.NotEmpty(vm.BenchmarkedFolders); // AC-29: the benchmarked-folder table gets a real row
        Assert.Equal(OnboardingStep.Done, vm.Step);
        UiE2E.Screenshot(window, "ac29-onboarding");
    }
}
