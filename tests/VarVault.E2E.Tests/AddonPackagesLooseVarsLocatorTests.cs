using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Activation;
using VarVault.Sdk.Import;
using VarVault.Sdk.Library;
using VarVault.Sdk.Settings;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

[Trait("Category", TestCategories.E2E)]
public sealed class AddonPackagesLooseVarsLocatorTests
{
    [Fact]
    public async Task Locate_reports_no_vam_path_when_unset()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var locator = scope.ServiceProvider.GetRequiredService<IAddonPackagesLooseVarsLocator>();
        var r = await locator.LocateAsync();
        Assert.Equal(LooseVarsLocateStatus.NoVamPath, r.Status);
        Assert.Contains("VaM path", r.Hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Locate_counts_loose_vars_in_active_profile()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var vam = new TempDirectory();
        var profile = ActivationPaths.ProfileDir(vam.Path, "Library installs");
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(Path.Combine(profile, "___VarsLink___"));
        File.WriteAllBytes(Path.Combine(profile, "Loose.Download.1.var"), [0x50, 0x4B]);
        File.WriteAllBytes(Path.Combine(profile, "___VarsLink___", "Install.Link.1.var"), [0x50, 0x4B]);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>()
            .SetAsync(SettingKeys.VamPath, vam.Path);
        // Create + switch profile so ActiveAsync resolves.
        var profiles = scope.ServiceProvider.GetRequiredService<IProfileService>();
        Assert.True((await profiles.CreateAsync("Library installs")).IsSuccess);
        Assert.True((await profiles.SwitchToAsync("Library installs")).IsSuccess);

        var locator = scope.ServiceProvider.GetRequiredService<IAddonPackagesLooseVarsLocator>();
        var r = await locator.LocateAsync();
        Assert.Equal(LooseVarsLocateStatus.Ready, r.Status);
        Assert.Equal(1, r.LooseVarCount);
        Assert.Equal("Library installs", r.ProfileName);
        Assert.Equal(profile, r.ProfilePath);
    }
}
