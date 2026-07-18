using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Sdk.Events;

namespace VarVault.Host.Internal;

/// <summary>
/// In-process event bus. Dispatches to every DI-registered <see cref="IEventHandler{TEvent}"/>
/// (cross-module reactions) plus lightweight inline subscribers (e.g. view-models).
/// </summary>
internal sealed class EventBus(IServiceProvider services) : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _inline = new();

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IEventHandler<TEvent>>())
            await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);

        if (_inline.TryGetValue(typeof(TEvent), out var list))
        {
            Delegate[] snapshot;
            lock (list) snapshot = list.ToArray();
            foreach (var handler in snapshot)
                ((Action<TEvent>)handler)(@event);
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IDomainEvent
    {
        var list = _inline.GetOrAdd(typeof(TEvent), _ => []);
        lock (list) list.Add(handler);
        return new Subscription(() => { lock (list) list.Remove(handler); });
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
