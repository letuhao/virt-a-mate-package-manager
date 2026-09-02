using Microsoft.Extensions.DependencyInjection;
using VarVault.Common;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Diagnostics;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Threading;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure;

/// <summary>
/// Registers cross-cutting infrastructure: clock, background job queue, single-writer
/// write queue, and a headless UI dispatcher. The catalog DB is added separately by
/// <see cref="Persistence.PersistenceRegistration.AddVarVaultPersistence"/> (Slice 1).
/// Modules consume all of this through SDK interfaces and never bind to it directly.
/// </summary>
public static class InfrastructureRegistration
{
    public static IServiceCollection AddVarVaultInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<IJobQueue>(_ => new BackgroundJobQueue());
        // Default no-op cross-process lock; persistence overrides it with a file lock once the DB path is known.
        services.AddSingleton<IGlobalWriteLock, NullWriteLock>();
        services.AddSingleton<IWriteQueue>(sp => new WriteQueue(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetService<IGlobalWriteLock>()));
        services.AddSingleton<IUiDispatcher, InlineUiDispatcher>();
        services.AddSingleton<IRepositoryEnumerator, RepositoryEnumerator>();
        services.AddSingleton<IVarInspector, VarInspector>();
        services.AddSingleton<Domain.Dedup.IFileHasher, Sha256FileHasher>();
        services.AddSingleton<Domain.Content.IEncodingFixer, Indexing.EncodingFixer>();
        services.AddSingleton<Domain.Content.IDuplicateEntryFixer, Indexing.DuplicateEntryFixer>();
        services.AddSingleton<Domain.Migration.IDurableFileMover, Indexing.DurableFileMover>();
        services.AddSingleton<Domain.Activation.ISymlinkService, Activation.SymlinkService>();
        services.AddSingleton<Domain.Activation.IVamProfileService, Activation.VamProfileService>();
        services.AddSingleton<Domain.Repositories.IDriveProfiler, Repositories.DriveProfiler>();
        services.AddSingleton<Domain.Repositories.IDriveBenchmark, Repositories.DriveBenchmark>();

        services.AddHealthChecks()
            .AddCheck<WriteQueueHealthCheck>("write-queue", tags: ["live"]);

        return services;
    }
}
