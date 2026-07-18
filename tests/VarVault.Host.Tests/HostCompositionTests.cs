using Microsoft.Extensions.DependencyInjection;
using VarVault.Host;
using VarVault.Sdk.Events;
using VarVault.Sdk.Modularity;
using VarVault.Sdk.Threading;

namespace VarVault.Host.Tests;

public class HostCompositionTests
{
    [Fact]
    public async Task Default_host_builds_and_loads_built_in_modules()
    {
        await using var host = Bootstrap.BuildDefault();

        Assert.Contains("Repositories", host.LoadedModules);
        Assert.Contains("Indexing", host.LoadedModules);
    }

    [Fact]
    public async Task Cross_cutting_services_resolve()
    {
        await using var host = Bootstrap.BuildDefault();

        Assert.NotNull(host.Services.GetRequiredService<IEventBus>());
        Assert.NotNull(host.Services.GetRequiredService<IJobQueue>());
        Assert.NotNull(host.Services.GetRequiredService<IWriteQueue>());
        Assert.NotNull(host.Services.GetRequiredService<IUiDispatcher>());
    }

    [Fact]
    public async Task Inline_subscribers_receive_published_events()
    {
        await using var host = Bootstrap.BuildDefault();
        var bus = host.Services.GetRequiredService<IEventBus>();

        var received = 0;
        using (bus.Subscribe<TestEvent>(_ => received++))
        {
            await bus.PublishAsync(new TestEvent());
            await bus.PublishAsync(new TestEvent());
        }
        await bus.PublishAsync(new TestEvent()); // after dispose — not counted

        Assert.Equal(2, received);
    }

    [Fact]
    public async Task Registered_event_handlers_are_invoked()
    {
        CountingHandler.Count = 0;
        var host = VarVaultHost.Build(HostOptions.Default, new HandlerModule());
        await using (host)
        {
            var bus = host.Services.GetRequiredService<IEventBus>();
            await bus.PublishAsync(new TestEvent());
        }

        Assert.Equal(1, CountingHandler.Count);
    }

    private sealed record TestEvent : IDomainEvent;

    private sealed class CountingHandler : IEventHandler<TestEvent>
    {
        public static int Count;
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return Task.CompletedTask;
        }
    }

    private sealed class HandlerModule : IModule
    {
        public string Name => "Handlers";
        public void Register(IServiceCollection services, IModuleContext context) =>
            services.AddSingleton<IEventHandler<TestEvent>, CountingHandler>();
    }
}
