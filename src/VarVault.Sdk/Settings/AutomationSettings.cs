namespace VarVault.Sdk.Settings;

/// <summary>
/// Automation policy read from settings. The sealed default is <b>propose, never auto-destroy</b>:
/// auto-migrate is off unless explicitly enabled. (Checklist 5.15, BE-P5.)
/// </summary>
public static class AutomationSettings
{
    /// <summary>Auto-migrate is opt-in; the default is propose-only.</summary>
    public const bool AutoMigrateDefault = false;

    public static Task<bool> IsAutoMigrateEnabledAsync(ISettingsService settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.GetBoolAsync(SettingKeys.AutoMigrate, AutoMigrateDefault, cancellationToken);
    }
}
