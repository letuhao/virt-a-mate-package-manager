using Microsoft.Extensions.DependencyInjection;
using VarVault.Sdk.Modularity;
using VarVault.Sdk.Repositories;

namespace VarVault.Modules.Repositories;

/// <summary>Repository registration, benchmarking, and tier management (Pillar 1).</summary>
public sealed class RepositoriesModule : IModule
{
    public string Name => "Repositories";

    public void Register(IServiceCollection services, IModuleContext context)
    {
        services.AddSingleton<IRepositoryService, RepositoryService>();
        _ = context;
    }
}
