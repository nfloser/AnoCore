namespace AnoCore.Abstractions.Messaging;

public interface IMessageTransport
{
    ValueTask<bool> TrySendAsync(
        MessageRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryClearAsync(
        MessageTarget target,
        MessageChannel channel,
        CancellationToken cancellationToken = default);
}
