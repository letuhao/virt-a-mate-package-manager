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
}
