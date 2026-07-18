namespace VarVault.Sdk.Modularity;

/// <summary>
/// An externally-loaded module (from a separate assembly in the plugins folder).
/// Deferred features (Hub browsing, load-into-VaM) ship as plugins against this SDK,
/// with zero changes to core.
/// </summary>
public interface IPlugin : IModule
{
    string Version { get; }
    string Author { get; }
}
