namespace AnoCore.Abstractions.Messaging;

public enum MessageDispatchStatus
{
    Delivered = 0,
    Suppressed = 1,
    Unavailable = 2,
}

public readonly record struct MessageDispatchResult(MessageDispatchStatus Status)
{
    public bool Delivered => Status is MessageDispatchStatus.Delivered;

    public static MessageDispatchResult DeliveredResult { get; }
        = new(MessageDispatchStatus.Delivered);

    public static MessageDispatchResult SuppressedResult { get; }
        = new(MessageDispatchStatus.Suppressed);

    public static MessageDispatchResult UnavailableResult { get; }
        = new(MessageDispatchStatus.Unavailable);
}
