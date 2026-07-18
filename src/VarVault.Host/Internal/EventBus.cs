using System.Collections.Concurrent;
using VarVault.Sdk.Events;

namespace VarVault.Host.Internal;

/// <summary>Simple thread-safe in-process event bus.</summary>
internal sealed class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

    public void Publish<TEvent>(TEvent @event) where TEvent : IDomainEvent
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var list))
            return;

        Delegate[] snapshot;
        lock (list) snapshot = list.ToArray();
        foreach (var handler in snapshot)
            ((Action<TEvent>)handler)(@event);
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IDomainEvent
    {
        var list = _handlers.GetOrAdd(typeof(TEvent), _ => []);
        lock (list) list.Add(handler);
        return new Subscription(() => { lock (list) list.Remove(handler); });
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
