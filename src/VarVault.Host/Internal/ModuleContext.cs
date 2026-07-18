using VarVault.Sdk.Modularity;

namespace VarVault.Host.Internal;

internal sealed class ModuleContext(string appName, string dataDirectory) : IModuleContext
{
    public string AppName => appName;
    public string DataDirectory => dataDirectory;
}
