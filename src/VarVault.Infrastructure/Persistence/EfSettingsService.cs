using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Sdk.Settings;

namespace VarVault.Infrastructure.Persistence;

/// <summary>EF implementation of <see cref="ISettingsService"/> over the <c>Setting</c> table (upsert). (X.13.)</summary>
public sealed class EfSettingsService(VarVaultDbContext db) : ISettingsService
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(key);
        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, cancellationToken).ConfigureAwait(false);
        return setting?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(key);
        Guard.NotNull(value);

        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken).ConfigureAwait(false);
        if (setting is null)
            db.Settings.Add(new Setting { Key = key, Value = value });
        else
            setting.Value = value;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback = false, CancellationToken cancellationToken = default)
    {
        var raw = await GetAsync(key, cancellationToken).ConfigureAwait(false);
        return raw is null ? fallback : bool.TryParse(raw, out var b) ? b : fallback;
    }

    public Task SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default) =>
        SetAsync(key, value ? bool.TrueString : bool.FalseString, cancellationToken);

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken).ConfigureAwait(false);
}
