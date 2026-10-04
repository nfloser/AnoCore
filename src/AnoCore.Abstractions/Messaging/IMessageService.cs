namespace AnoCore.Abstractions.Messaging;

public interface IMessageService
{
    ValueTask<MessageDispatchResult> SendAsync(
        MessageRequest request,
        CancellationToken cancellationToken = default);
}
