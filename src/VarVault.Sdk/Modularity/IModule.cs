using Microsoft.Extensions.DependencyInjection;

namespace VarVault.Sdk.Modularity;

/// <summary>
/// A composable feature module. The Host calls <see cref="Register"/> on each module
/// into a single container. Modules communicate only through SDK contracts + events —
/// never by referencing each other.
/// </summary>
public interface IModule
{
    /// <summary>Stable, unique module name (e.g. "Indexing").</summary>
    string Name { get; }

    /// <summary>Register this module's services and event subscriptions.</summary>
    void Register(IServiceCollection services, IModuleContext context);
}
