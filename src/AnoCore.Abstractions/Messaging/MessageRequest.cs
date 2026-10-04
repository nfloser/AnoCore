namespace AnoCore.Abstractions.Messaging;

public sealed record MessageRequest
{
    public const int MaxTextLength = 1024;
    public const int MaxPriority = 1000;
    public static readonly TimeSpan MinDuration = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(1);

    public MessageRequest(
        MessageTarget target,
        MessageChannel channel,
        string text,
        int priority = 0,
        TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Message text cannot be empty.", nameof(text));
        }

        if (text.Length > MaxTextLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(text), $"Message text cannot exceed {MaxTextLength} characters.");
        }

        if (priority is < 0 or > MaxPriority)
        {
            throw new ArgumentOutOfRangeException(
                nameof(priority), $"Message priority must be between 0 and {MaxPriority}.");
        }

        if (channel is MessageChannel.Chat && duration is not null)
        {
            throw new ArgumentException("Chat messages do not support a display duration.", nameof(duration));
        }

        if (duration is { } bounded
            && (bounded < MinDuration || bounded > MaxDuration))
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                $"Message duration must be between {MinDuration.TotalMilliseconds:0} ms and {MaxDuration.TotalSeconds:0} s.");
        }

        Target = target;
        Channel = channel;
        Text = text;
        Priority = priority;
        Duration = duration;
    }

    public MessageTarget Target { get; }

    public MessageChannel Channel { get; }

    public string Text { get; }

    public int Priority { get; }

    public TimeSpan? Duration { get; }
}
