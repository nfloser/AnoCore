using AnoCore.Abstractions.Events;

namespace AnoCore.Runtime.Events;

public sealed class AnoEventBus : IAnoEventBus
{
    private readonly object _sync = new();
    private readonly Dictionary<Type, List<SubscriptionEntry>> _subscriptions = [];

    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler)
        where TEvent : IAnoEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        var eventType = typeof(TEvent);
        var id = Guid.NewGuid();
        var entry = new SubscriptionEntry(
            id,
            (payload, cancellationToken) => handler((TEvent)payload, cancellationToken));

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(eventType, out var entries))
            {
                entries = [];
                _subscriptions[eventType] = entries;
            }

            entries.Add(entry);
        }

        return new EventSubscription(() => Unsubscribe(eventType, id));
    }

    public async ValueTask PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IAnoEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        cancellationToken.ThrowIfCancellationRequested();

        SubscriptionEntry[] subscribers;
        lock (_sync)
        {
            subscribers = _subscriptions.TryGetValue(typeof(TEvent), out var entries)
                ? entries.ToArray()
                : [];
        }

        List<Exception>? failures = null;
        foreach (var subscriber in subscribers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await subscriber.Invoke(@event, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more AnoCore event subscribers failed.", failures);
        }
    }

    private void Unsubscribe(Type eventType, Guid id)
    {
        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(eventType, out var entries))
            {
                return;
            }

            entries.RemoveAll(entry => entry.Id == id);
            if (entries.Count == 0)
            {
                _subscriptions.Remove(eventType);
            }
        }
    }

    private sealed record SubscriptionEntry(
        Guid Id,
        Func<object, CancellationToken, ValueTask> Invoke);

    private sealed class EventSubscription : IDisposable
    {
        private readonly Action _unsubscribe;
        private int _disposed;

        public EventSubscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _unsubscribe();
            }
        }
    }
}
