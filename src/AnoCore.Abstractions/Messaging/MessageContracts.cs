using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Messaging;

public enum MessageChannel
{
    Chat = 1,
    Center = 2,
    Console = 3,
}

public sealed record MessageContent
{
    private const int MaximumArguments = 32;
    private const int MaximumRawLength = 1024;

    private MessageContent(
        string? localizationKey,
        string? rawText,
        IReadOnlyDictionary<string, object?> arguments)
    {
        LocalizationKey = localizationKey;
        RawText = rawText;
        Arguments = arguments;
    }

    public string? LocalizationKey { get; }
    public string? RawText { get; }
    public IReadOnlyDictionary<string, object?> Arguments { get; }
    public bool IsLocalized => LocalizationKey is not null;

    public static MessageContent Localized(
        string key,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var normalizedKey = Identifier(key, nameof(key), 128);
        var values = arguments ?? new Dictionary<string, object?>();
        if (values.Count > MaximumArguments)
            throw new ArgumentException(
                $"Messages support at most {MaximumArguments} localization arguments.",
                nameof(arguments));

        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            var argumentKey = Identifier(pair.Key, nameof(arguments), 64);
            if (!normalized.TryAdd(argumentKey, pair.Value))
                throw new ArgumentException(
                    "Localization argument names must be unique.",
                    nameof(arguments));
        }

        return new MessageContent(normalizedKey, null, normalized);
    }

    public static MessageContent Raw(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Raw message text is required.", nameof(text));
        var normalized = text.Trim();
        if (normalized.Length > MaximumRawLength)
            throw new ArgumentException(
                $"Raw messages must be at most {MaximumRawLength} characters.",
                nameof(text));
        if (normalized.Any(char.IsControl))
            throw new ArgumentException(
                "Raw messages cannot contain control characters.",
                nameof(text));
        return new MessageContent(null, normalized,
            new Dictionary<string, object?>(StringComparer.Ordinal));
    }

    private static string Identifier(string? value, string parameterName, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A message identifier is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximum
            || normalized.Any(character =>
                !(char.IsAsciiLetterOrDigit(character)
                    || character is '.' or '-' or '_')))
        {
            throw new ArgumentException(
                "Message identifiers contain unsupported characters.",
                parameterName);
        }
        return normalized;
    }
}

public interface IMessageComposer
{
    string Compose(MessageContent content, string? locale = null);
}

public interface IMessageLocaleResolver
{
    string ServerLocale { get; }

    string Resolve(PlayerSnapshot player);
}

public interface IMessageTransport
{
    ValueTask SendAsync(
        PlayerSnapshot expectedPlayer,
        MessageChannel channel,
        string message,
        CancellationToken cancellationToken = default);
}

public interface IMessageService
{
    ValueTask<bool> SendAsync(
        PlayerId playerId,
        MessageChannel channel,
        MessageContent content,
        CancellationToken cancellationToken = default);

    ValueTask<int> BroadcastAsync(
        MessageChannel channel,
        MessageContent content,
        CancellationToken cancellationToken = default);
}
