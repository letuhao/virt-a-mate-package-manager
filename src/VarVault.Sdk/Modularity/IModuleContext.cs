namespace VarVault.Sdk.Modularity;

/// <summary>Host-provided context available to a module during registration.</summary>
public interface IModuleContext
{
    /// <summary>Application name (branding, log scope).</summary>
    string AppName { get; }

    /// <summary>Writable directory for the app's data (catalog DB, thumbnails, logs).</summary>
    string DataDirectory { get; }
}
