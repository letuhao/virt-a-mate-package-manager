using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VarVault.Domain.Entities;

namespace VarVault.Infrastructure.Persistence;

/// <summary>
/// The catalog database context. Entity configurations live in
/// <c>Persistence/Configurations</c> and are applied from this assembly.
/// </summary>
public class VarVaultDbContext(DbContextOptions<VarVaultDbContext> options) : DbContext(options)
{
    // Storage
    public DbSet<Repository> Repositories => Set<Repository>();

    // Catalog
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<VarFile> VarFiles => Set<VarFile>();
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    public DbSet<PackageContentCount> PackageContentCounts => Set<PackageContentCount>();

    // Dependencies
    public DbSet<Dependency> Dependencies => Set<Dependency>();
    public DbSet<UserSave> UserSaves => Set<UserSave>();
    public DbSet<SaveDependency> SaveDependencies => Set<SaveDependency>();

    // Analyzer
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<UsageStat> UsageStats => Set<UsageStat>();
    public DbSet<MigrationJob> MigrationJobs => Set<MigrationJob>();

    // Activation & presets
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ActivationLink> ActivationLinks => Set<ActivationLink>();
    public DbSet<LoadingPreset> LoadingPresets => Set<LoadingPreset>();
    public DbSet<PresetMember> PresetMembers => Set<PresetMember>();
    public DbSet<VarAlias> VarAliases => Set<VarAlias>();

    // Discovery
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<PackageTag> PackageTags => Set<PackageTag>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionMember> CollectionMembers => Set<CollectionMember>();
    public DbSet<ContentItemPref> ContentItemPrefs => Set<ContentItemPref>();

    // Read model, trash, config
    public DbSet<PackageListItem> PackageListItems => Set<PackageListItem>();
    public DbSet<TrashItem> TrashItems => Set<TrashItem>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<ActivityEntry> ActivityEntries => Set<ActivityEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VarVaultDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Persist every DateTime as UTC ISO-8601 text and read it back with Kind=Utc, so the
        // catalog is timezone-stable across machines/restores. (UsageEvent uses an epoch long instead.)
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }
}

internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => ToUtc(v),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
{
    // The app's time convention is UTC (IClock.UtcNow). Local → convert; Unspecified → assume UTC
    // (never shift, so a default(DateTime) can't underflow DateTime.MinValue).
    private static DateTime ToUtc(DateTime v) => v.Kind switch
    {
        DateTimeKind.Utc => v,
        DateTimeKind.Local => v.ToUniversalTime(),
        _ => DateTime.SpecifyKind(v, DateTimeKind.Utc),
    };
}

internal sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v == null ? null : ToUtc(v.Value),
    v => v == null ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
{
    private static DateTime ToUtc(DateTime v) => v.Kind switch
    {
        DateTimeKind.Utc => v,
        DateTimeKind.Local => v.ToUniversalTime(),
        _ => DateTime.SpecifyKind(v, DateTimeKind.Utc),
    };
}
