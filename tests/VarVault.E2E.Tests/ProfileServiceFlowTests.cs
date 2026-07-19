using Microsoft.Extensions.DependencyInjection;
using VarVault.Sdk.Library;
using VarVault.Sdk.Settings;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// BE-N7 · Profile facade: reads the VaM root from settings, creates/lists profiles, and switches by
/// repointing the AddonPackages symlink (skips the positive path where privilege is absent). (16-checklist BE-N7.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ProfileServiceFlowTests
{
    [Fact]
    public async Task Create_list_and_switch_profiles()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var vamRoot = new TempDirectory();

        using var scope = host.Host.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        await settings.SetAsync(SettingKeys.VamPath, vamRoot.Path);

        var svc = scope.ServiceProvider.GetRequiredService<IProfileService>();
        Assert.True((await svc.CreateAsync("Default")).IsSuccess);
        Assert.True((await svc.CreateAsync("Studio")).IsSuccess);

        var list = await svc.ListAsync();
        Assert.Equal(["Default", "Studio"], list);

        var switched = await svc.SwitchToAsync("Default");
        if (!switched.IsSuccess && switched.Error.Code == "symlink.privilege")
            return; // sandbox lacks symlink-create privilege — positive path skipped

        Assert.True(switched.IsSuccess);
        Assert.Equal("Default", await svc.ActiveAsync());
    }

    [Fact]
    public async Task List_is_empty_without_vam_path()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IProfileService>();
        Assert.Empty(await svc.ListAsync());
        Assert.False((await svc.SwitchToAsync("X")).IsSuccess);
    }
}
