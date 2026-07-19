using Microsoft.Extensions.DependencyInjection;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Modularity;

namespace VarVault.Modules.Indexing;

/// <summary>Staged, incremental indexing: identity, classification, signatures, previews (Pillar 3).</summary>
public sealed class IndexingModule : IModule
{
    public string Name => "Indexing";

    public void Register(IServiceCollection services, IModuleContext context)
    {
        // The orchestrator binds only to Domain/SDK seams; Infrastructure provides the
        // IRepositoryEnumerator/IVarInspector/ICatalogStore implementations via DI.
        services.AddSingleton<IIndexingService, IndexingService>();
        services.AddSingleton<IIndexOrchestrator, IndexOrchestrator>();
        _ = context;
    }
}
