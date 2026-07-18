using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using VarVault.Common;
using VarVault.Host;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Modularity;

namespace VarVault.TestKit;

/// <summary>
/// A fully-composed VarVault host for integration/e2e tests, with deterministic seams
/// (fake clock, temp data dir, captured logs) and optional persistence + extra modules.
/// </summary>
public sealed class TestHost : IAsyncDisposable
{
    public VarVaultHost Host { get; }
    public FakeClock Clock { get; }
    public TempDirectory DataDirectory { get; }
    public CapturingLoggerProvider Logs { get; }

    private TestHost(VarVaultHost host, FakeClock clock, TempDirectory dir, CapturingLoggerProvider logs)
    {
        Host = host;
        Clock = clock;
        DataDirectory = dir;
        Logs = logs;
    }

    public static TestHost Create(
        bool withPersistence = false,
        Action<IServiceCollection>? configure = null,
        params IModule[] extraModules)
    {
        var clock = new FakeClock();
        var dir = new TempDirectory();
        var logs = new CapturingLoggerProvider();

        var modules = Bootstrap.BuiltInModules().Concat(extraModules).ToArray();

        var host = VarVaultHost.Build(
            new HostOptions("VarVaultTest", dir.Path),
            services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
                services.AddLogging(b => b.AddProvider(logs));
                if (withPersistence)
                    services.AddVarVaultPersistence(dir.File("catalog.db"));
                configure?.Invoke(services);
            },
            modules);

        if (withPersistence)
        {
            // Stand in for Slice-1's migration so the DB is real and connectable.
            using var scope = host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<VarVaultDbContext>().Database.EnsureCreated();
        }

        return new TestHost(host, clock, dir, logs);
    }

    public T Get<T>() where T : notnull => Host.Services.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        DataDirectory.Dispose();
    }
}
