using Microsoft.Extensions.DependencyInjection;
using VarVault.Host;
using VarVault.Sdk.Events;

namespace VarVault.Host.Tests;

public class HostCompositionTests
{
    [Fact]
    public void Default_host_builds_and_loads_built_in_modules()
    {
        using var host = Bootstrap.BuildDefault();

        Assert.Contains("Repositories", host.LoadedModules);
        Assert.Contains("Indexing", host.LoadedModules);
    }

    [Fact]
    public void Event_bus_delivers_to_subscribers()
    {
        using var host = Bootstrap.BuildDefault();
        var bus = host.Services.GetRequiredService<IEventBus>();

        var received = 0;
        using (bus.Subscribe<TestEvent>(_ => received++))
        {
            bus.Publish(new TestEvent());
            bus.Publish(new TestEvent());
        }
        bus.Publish(new TestEvent()); // after dispose — not counted

        Assert.Equal(2, received);
    }

    private sealed record TestEvent : IDomainEvent;
}
