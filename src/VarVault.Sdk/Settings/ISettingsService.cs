namespace VarVault.Sdk.Settings;

/// <summary>
/// Persisted app settings (VaM path, tier definitions, policies, thresholds, fix-on-import defaults).
/// Backed by the <c>Setting</c> key/value table. (Checklist X.13.)
/// </summary>
public interface ISettingsService
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<bool> GetBoolAsync(string key, bool fallback = false, CancellationToken cancellationToken = default);
    Task SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Well-known setting keys.</summary>
public static class SettingKeys
{
    public const string VamPath = "vam.path";
    public const string AutoMigrate = "policy.auto_migrate";
    public const string FixOnImport = "policy.fix_on_import";
    public const string HotThresholdDays = "tiers.hot_threshold_days";
    public const string AutoRebalance = "automation.auto_rebalance";
}
