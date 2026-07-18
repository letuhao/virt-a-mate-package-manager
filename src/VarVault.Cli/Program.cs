using VarVault.Host;

await using var host = Bootstrap.BuildDefault();

Console.WriteLine($"VarVault {typeof(VarVaultHost).Assembly.GetName().Version}");
Console.WriteLine($"Host started. {host.LoadedModules.Count} module(s) loaded:");
foreach (var module in host.LoadedModules)
    Console.WriteLine($"  - {module}");
