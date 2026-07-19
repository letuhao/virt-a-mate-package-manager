using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Settings;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>Automation defaults to propose-only: auto-migrate is off unless explicitly enabled. (5.15/BE-P5.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class AutomationSettingsTests
{
    [Fact]
    public async Task Auto_migrate_defaults_to_off_and_is_opt_in()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();
        var settings = new EfSettingsService(db);

        Assert.False(await AutomationSettings.IsAutoMigrateEnabledAsync(settings)); // default = propose

        await settings.SetBoolAsync(SettingKeys.AutoMigrate, true);
        Assert.True(await AutomationSettings.IsAutoMigrateEnabledAsync(settings));  // explicit opt-in
    }
}
