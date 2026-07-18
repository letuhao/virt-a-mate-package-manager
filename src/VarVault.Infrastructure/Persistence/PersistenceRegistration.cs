using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Indexing;
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
            options.UseSqlite($"Data Source={databasePath}"));

        services.AddScoped<IUnitOfWork>(sp => new EfUnitOfWork(sp.GetRequiredService<VarVaultDbContext>()));
        services.AddScoped<ICatalogStore, EfCatalogStore>();
        services.AddSingleton<CatalogDatabaseInitializer>();

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
