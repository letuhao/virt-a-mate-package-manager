namespace VarVault.Sdk.Events;

/// <summary>Marker for an in-process domain event crossing module boundaries.</summary>
public interface IDomainEvent;

/// <summary>
/// In-process publish/subscribe bus. Modules react to each other's events without
/// coupling — the only sanctioned cross-module communication besides SDK service calls.
/// </summary>
public interface IEventBus
{
    void Publish<TEvent>(TEvent @event) where TEvent : IDomainEvent;

    /// <summary>Subscribe a handler; dispose the returned token to unsubscribe.</summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IDomainEvent;
}
