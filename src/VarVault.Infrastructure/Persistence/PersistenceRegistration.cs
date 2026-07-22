using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Common;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Indexing;
using VarVault.Domain.Repositories;
using VarVault.Domain.Safety;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Repositories;
using VarVault.Infrastructure.Safety;
using VarVault.Sdk.Indexer;
using VarVault.Sdk.Persistence;

namespace VarVault.Infrastructure.Persistence;

/// <summary>
/// Registers the catalog DB. Called by Slice 1 (not the bare foundation) once entities
/// exist. Writes must still go through <see cref="Sdk.Threading.IWriteQueue"/>.
/// </summary>
public static class PersistenceRegistration
{
    public static IServiceCollection AddVarVaultPersistence(this IServiceCollection services, string databasePath)
    {
        services.AddDbContext<VarVaultDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}")
                .AddInterceptors(new SqlitePragmaInterceptor()));

        // Cross-process single-writer: every WriteQueue action (GUI or indexer) serializes on this file lock,
        // so at most one process writes the catalog at any instant. Overrides the no-op default. (A12.)
        var writeLockPath = Threading.FileGlobalWriteLock.PathFor(databasePath);
        services.AddSingleton<Sdk.Threading.IGlobalWriteLock>(_ => new Threading.FileGlobalWriteLock(writeLockPath));

        services.AddScoped<IUnitOfWork>(sp => new EfUnitOfWork(sp.GetRequiredService<VarVaultDbContext>()));
        services.AddScoped<ICatalogStore, EfCatalogStore>();
        services.AddScoped<IScanLedger, ScanLedger>();
        services.AddScoped<IDurableDirtySet, DurableDirtySet>();
        services.AddScoped<IStreamIndexer, StreamIndexer>();
        services.AddSingleton<OneHandleVarInspector>();
        services.AddSingleton<IIndexerWorker, IndexerWorker>();
        services.AddSingleton<IIndexerClient, InProcessIndexerClient>();

        // Staged-index pass-2: preview extraction into a packed, SHARDED thumbnail store (thumbnails/thumb_*.db) so it
        // scales to 700k items / tens of GB — per-shard VACUUM, parallel writes, isolated corruption. (1.23/1.32/1.33)
        var thumbsDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".", "thumbnails");
        services.AddSingleton<Domain.Indexing.IThumbnailStore>(_ => new Indexing.ShardedThumbnailStore(thumbsDir));
        services.AddSingleton<Indexing.PreviewExtractor>();
        services.AddScoped<Domain.Indexing.IPreviewIndexer, Indexing.EfPreviewIndexer>();
        services.AddScoped<IDependencyResolver, EfDependencyResolver>();
        services.AddScoped<Domain.Analyzer.IUsageAnalyzer, EfUsageAnalyzer>();
        services.AddScoped<IDependencyGraph, EfDependencyGraph>();
        services.AddScoped<IReferenceQuery, EfReferenceQuery>();
        services.AddScoped<UserSaveScanner>();
        services.AddScoped<CatalogReconciler>();
        services.AddScoped<MigrationRunner>();
        services.AddScoped<EncodingFixCoordinator>();
        services.AddSingleton<Domain.Analyzer.FreeSpaceLedger>();

        // Trash lives alongside the DB so restore survives DB loss (per-item manifests).
        var trashRoot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".", "trash");
        services.AddSingleton<ITrashService>(sp => new FileTrashService(trashRoot, sp.GetRequiredService<IClock>()));
        services.AddScoped<IRepositoryStore, EfRepositoryStore>();
        services.AddScoped<Sdk.Library.ILibraryQueryService, Library.EfLibraryQueryService>();
        services.AddScoped<Sdk.Library.IMissingDepsQuery, Library.EfMissingDepsQuery>();
        services.AddScoped<Sdk.Library.IMissingLogResolver, Library.EfMissingLogResolver>();
        services.AddScoped<Sdk.Library.IInstalledDepsRepair, Library.EfInstalledDepsRepair>();
        services.AddScoped<Sdk.Library.IAnalyticsService, Library.EfAnalyticsService>();
        services.AddScoped<Sdk.Library.IDashboardService, Library.EfDashboardService>();
        services.AddScoped<Sdk.Library.ITieringService, Library.EfTieringService>();
        services.AddScoped<Sdk.Library.IMigrationService, Library.EfMigrationService>();
        services.AddScoped<Sdk.Library.IReclaimService, Library.EfReclaimService>();
        services.AddScoped<Sdk.Library.IIntakeService, Library.EfIntakeService>();
        // Import feature (doc 31 Phase 5.8)
        services.AddScoped<Sdk.Import.IArchiveExtractor, Import.ArchiveExtractor>();
        services.AddScoped<Sdk.Import.IImportHistoryStore, Library.EfImportHistoryStore>();
        services.AddScoped<Sdk.Import.IImportService, Library.EfImportService>();
        services.AddScoped<Sdk.Import.IAddonPackagesLooseVarsLocator, Library.AddonPackagesLooseVarsLocator>();
        services.AddScoped<Sdk.Library.IHealthService, Library.EfHealthService>();
        services.AddScoped<SqliteDatabaseBackup>();
        services.AddScoped<Sdk.Library.ITrashQueryService, Library.EfTrashQueryService>();
        services.AddScoped<Sdk.Library.IProfileService, Library.EfProfileService>();
        services.AddScoped<Sdk.Library.IProfilePackageLinkService, Library.EfProfilePackageLinkService>();
        services.AddScoped<Sdk.Library.IProposalService, Library.EfProposalService>();
        services.AddScoped<Sdk.Library.IPackageDetailQuery, Library.EfPackageDetailQuery>();
        services.AddScoped<Sdk.Library.ILibraryActionService, Library.EfLibraryActionService>();
        services.AddScoped<Sdk.Library.IAliasService, Library.EfAliasService>();
        services.AddScoped<Sdk.Library.ITagService, Library.EfTagService>();
        services.AddScoped<Sdk.Library.IOnboardingService, Library.EfOnboardingService>();
        services.AddScoped<Sdk.Library.ICommandPaletteService, Library.EfCommandPaletteService>();
        services.AddScoped<Sdk.Settings.ISettingsService, EfSettingsService>();
        services.AddScoped<Sdk.Presets.IPresetService, Indexing.EfPresetService>();
        services.AddScoped<Sdk.Activation.IActivityLog, Indexing.EfActivityLog>();
        services.AddScoped<Sdk.Activation.IActivationService, Indexing.EfActivationService>();
        services.AddSingleton<CatalogDatabaseInitializer>();
        services.AddScoped<SqliteDatabaseBackup>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        return services;
    }
}

internal sealed class EfUnitOfWork(VarVaultDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
