using AnoCore.Abstractions.Messaging;

namespace AnoCore.Runtime.Messaging;

public sealed class MessageService : IMessageService, IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<MessageSlot, ActiveMessage> _active = [];
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private IMessageTransport? _transport;
    private long _version;
    private bool _disposed;

    public MessageService(Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _delay = delay ?? static (duration, token) => Task.Delay(duration, token);
    }

    public IDisposable AttachTransport(IMessageTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_transport is not null)
            {
                throw new InvalidOperationException("A message transport is already attached.");
            }

            _transport = transport;
            return new TransportHandle(this, transport);
        }
    }

    public async ValueTask<MessageDispatchResult> SendAsync(
        MessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IMessageTransport? transport;
            MessageSlot? slot = null;
            long version = 0;

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                transport = _transport;
                if (transport is null)
                {
                    return MessageDispatchResult.UnavailableResult;
                }

                if (request.Channel is not MessageChannel.Chat && request.Duration is not null)
                {
                    slot = new MessageSlot(request.Target, request.Channel);
                    if (_active.TryGetValue(slot, out var current)
                        && current.Priority > request.Priority)
                    {
                        return MessageDispatchResult.SuppressedResult;
                    }

                    version = ++_version;
                    _active[slot] = new ActiveMessage(version, request.Priority);
                }
            }

            var delivered = await transport.TrySendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!delivered)
            {
                if (slot is not null)
                {
                    RemoveIfCurrent(slot, version);
                }

                return MessageDispatchResult.UnavailableResult;
            }

            if (slot is not null && request.Duration is { } duration)
            {
                _ = ExpireAsync(slot, request.Target, request.Channel, version, duration);
            }

            return MessageDispatchResult.DeliveredResult;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _transport = null;
            _active.Clear();
            _lifetime.Cancel();
        }

        _lifetime.Dispose();
        _sendGate.Dispose();
    }

    private async Task ExpireAsync(
        MessageSlot slot,
        MessageTarget target,
        MessageChannel channel,
        long version,
        TimeSpan duration)
    {
        try
        {
            await _delay(duration, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }

        IMessageTransport? transport;
        lock (_gate)
        {
            if (_disposed
                || !_active.TryGetValue(slot, out var current)
                || current.Version != version)
            {
                return;
            }

            _active.Remove(slot);
            transport = _transport;
        }

        if (transport is null)
        {
            return;
        }

        try
        {
            await transport.TryClearAsync(target, channel, _lifetime.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private void RemoveIfCurrent(MessageSlot slot, long version)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(slot, out var current)
                && current.Version == version)
            {
                _active.Remove(slot);
            }
        }
    }

    private void Detach(IMessageTransport transport)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_transport, transport))
            {
                _transport = null;
                _active.Clear();
            }
        }
    }

    private sealed record MessageSlot(MessageTarget Target, MessageChannel Channel);

    private sealed record ActiveMessage(long Version, int Priority);

    private sealed class TransportHandle(MessageService owner, IMessageTransport transport) : IDisposable
    {
        private MessageService? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.Detach(transport);
    }
}
