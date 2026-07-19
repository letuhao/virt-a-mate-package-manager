using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Settings;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>Settings persist as key/value with upsert and typed bool helpers. (X.13.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class EfSettingsServiceTests
{
    [Fact]
    public async Task Set_get_and_overwrite()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();
        var settings = new EfSettingsService(db);

        await settings.SetAsync(SettingKeys.VamPath, @"C:\VaM");
        Assert.Equal(@"C:\VaM", await settings.GetAsync(SettingKeys.VamPath));

        await settings.SetAsync(SettingKeys.VamPath, @"D:\VaM");
        Assert.Equal(@"D:\VaM", await settings.GetAsync(SettingKeys.VamPath)); // upsert, not duplicate
    }

    [Fact]
    public async Task Missing_key_is_null_and_bool_fallback_applies()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();
        var settings = new EfSettingsService(db);

        Assert.Null(await settings.GetAsync("nope"));
        Assert.True(await settings.GetBoolAsync("policy.auto_migrate", fallback: true));

        await settings.SetBoolAsync(SettingKeys.AutoMigrate, false);
        Assert.False(await settings.GetBoolAsync(SettingKeys.AutoMigrate, fallback: true));
    }

    [Fact]
    public async Task Get_all_returns_every_setting()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();
        var settings = new EfSettingsService(db);

        await settings.SetAsync("a", "1");
        await settings.SetAsync("b", "2");

        var all = await settings.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal("1", all["a"]);
    }
}
