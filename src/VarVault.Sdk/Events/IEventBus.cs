namespace VarVault.Sdk.Events;

/// <summary>Marker for an in-process domain event crossing module boundaries.</summary>
public interface IDomainEvent;

/// <summary>
/// A DI-registered handler for a domain event. Modules react to each other's events by
/// registering handlers — the sanctioned cross-module communication besides SDK service calls.
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-process publish/subscribe bus. Dispatches an event to every DI-registered
/// <see cref="IEventHandler{TEvent}"/> plus any lightweight inline subscribers.
/// </summary>
public interface IEventBus
{
    /// <summary>Publish to all handlers; completes when they have all run.</summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;

    /// <summary>Subscribe a lightweight inline handler (e.g. a view-model); dispose to unsubscribe.</summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IDomainEvent;
}
