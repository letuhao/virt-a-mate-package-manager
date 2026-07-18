using Microsoft.Extensions.DependencyInjection;
using VarVault.Sdk.Modularity;

namespace VarVault.Modules.Indexing;

/// <summary>Staged, incremental indexing: identity, classification, signatures, previews (Pillar 3).</summary>
public sealed class IndexingModule : IModule
{
    public string Name => "Indexing";

    public void Register(IServiceCollection services, IModuleContext context)
    {
        // Slice 1: register IIndexingService, the staged pipeline, signature + encoding engines.
        _ = services;
        _ = context;
    }
}
