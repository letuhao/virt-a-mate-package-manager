using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Infrastructure.Persistence;
using VarVault.Modules.Indexing;
using VarVault.Modules.Repositories;
using VarVault.Sdk.Modularity;

namespace VarVault.Host;

/// <summary>
/// Known built-in modules and the default host composition. Front-ends (CLI, GUI)
/// call <see cref="BuildDefault"/> and depend only on the Host, not on each module.
/// </summary>
public static class Bootstrap
{
    /// <summary>The built-in feature modules, in a deterministic order.</summary>
    public static IReadOnlyList<IModule> BuiltInModules() =>
    [
        new RepositoriesModule(),
        new IndexingModule(),
    ];

    public static VarVaultHost BuildDefault(HostOptions? options = null) =>
        VarVaultHost.Build(options ?? HostOptions.Default, [.. BuiltInModules()]);

    /// <summary>
    /// Full app composition: built-in modules + the catalog DB under <paramref name="dataDirectory"/>,
    /// migrated and ready. Front-ends call this so they never bind to Infrastructure directly.
    /// </summary>
    public static VarVaultHost BuildApp(string dataDirectory)
    {
        var dbPath = System.IO.Path.Combine(dataDirectory, "catalog.db");
        var host = VarVaultHost.Build(
            new HostOptions("VarVault", dataDirectory),
            services => services.AddVarVaultPersistence(dbPath),
            [.. BuiltInModules()]);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var initializer = scope.ServiceProvider.GetRequiredService<CatalogDatabaseInitializer>();
        var prepared = initializer.PrepareAsync(db, dbPath, repositoryPaths: []).GetAwaiter().GetResult();
        if (prepared.IsFailure)
            throw new InvalidOperationException($"Catalog prepare failed: {prepared.Error}");
        return host;
    }
}
