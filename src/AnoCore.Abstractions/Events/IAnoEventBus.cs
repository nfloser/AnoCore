namespace AnoCore.Abstractions.Events;

public interface IAnoEventBus
{
    IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler)
        where TEvent : IAnoEvent;

    ValueTask PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IAnoEvent;
}
