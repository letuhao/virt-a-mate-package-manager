using Microsoft.Extensions.DependencyInjection;
using VarVault.Sdk.Modularity;

namespace VarVault.Modules.Repositories;

/// <summary>Repository registration, benchmarking, and tier management (Pillar 1).</summary>
public sealed class RepositoriesModule : IModule
{
    public string Name => "Repositories";

    public void Register(IServiceCollection services, IModuleContext context)
    {
        // Slice 1: register IRepositoryService, benchmark engine, capacity monitor.
        _ = services;
        _ = context;
    }
}
