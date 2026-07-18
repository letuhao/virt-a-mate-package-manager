using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using VarVault.Host.Internal;
using VarVault.Host.Logging;
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
/// The composition root. Builds one DI container from logging + shared infrastructure +
/// the event bus + every module, validating on build. GUI, CLI, and tests all run through it.
/// </summary>
public sealed class VarVaultHost : IAsyncDisposable
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
        => Build(options, configure: null, modules);

    /// <summary>
    /// Compose the host, applying <paramref name="configure"/> after modules register — the
    /// seam tests use to override services (fake clock, in-memory adapters, extra probes).
    /// </summary>
    public static VarVaultHost Build(HostOptions options, Action<IServiceCollection>? configure, params IModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modules);

        var services = new ServiceCollection();
        var context = new ModuleContext(options.AppName, options.DataDirectory);

        var serilog = LoggingSetup.Create(options.AppName, options.DataDirectory);
        services.AddLogging(builder => builder.AddSerilog(serilog, dispose: true));

        services.AddSingleton<IModuleContext>(context);
        services.AddSingleton<IEventBus>(sp => new EventBus(sp));
        services.AddVarVaultInfrastructure();

        var names = new List<string>(modules.Length);
        foreach (var module in modules)
        {
            module.Register(services, context);
            services.AddSingleton(module);
            names.Add(module.Name);
        }

        configure?.Invoke(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        provider.GetRequiredService<ILogger<VarVaultHost>>()
            .LogInformation("VarVault host composed with {ModuleCount} modules: {Modules}",
                names.Count, string.Join(", ", names));

        return new VarVaultHost(provider, names.AsReadOnly());
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
