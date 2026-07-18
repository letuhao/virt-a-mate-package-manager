using Microsoft.Extensions.DependencyInjection;
using VarVault.Host.Internal;
using VarVault.Infrastructure;
using VarVault.Sdk.Events;
using VarVault.Sdk.Modularity;

namespace VarVault.Host;

/// <summary>Host configuration.</summary>
public sealed record HostOptions(string AppName, string DataDirectory)
{
    public static HostOptions Default =>
        new("VarVault", AppContext.BaseDirectory);
}

/// <summary>
/// The composition root. Builds one DI container from the shared infrastructure plus
/// every module, validating on build. GUI, CLI, and tests all run through this.
/// </summary>
public sealed class VarVaultHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public IServiceProvider Services => _provider;
    public IReadOnlyList<string> LoadedModules { get; }

    private VarVaultHost(ServiceProvider provider, IReadOnlyList<string> loadedModules)
    {
        _provider = provider;
        LoadedModules = loadedModules;
    }

    /// <summary>Compose the host from the given built-in modules.</summary>
    public static VarVaultHost Build(HostOptions options, params IModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modules);

        var services = new ServiceCollection();
        var context = new ModuleContext(options.AppName, options.DataDirectory);

        services.AddSingleton<IModuleContext>(context);
        services.AddSingleton<IEventBus, EventBus>();
        services.AddVarVaultInfrastructure();

        var names = new List<string>(modules.Length);
        foreach (var module in modules)
        {
            module.Register(services, context);
            services.AddSingleton(module);
            names.Add(module.Name);
        }

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        return new VarVaultHost(provider, names.AsReadOnly());
    }

    public void Dispose() => _provider.Dispose();
}
