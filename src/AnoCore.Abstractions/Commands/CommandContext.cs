using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Commands;

public sealed class CommandContext
{
    public CommandContext(
        PlayerId? caller,
        IReadOnlyList<string> arguments,
        string rawInput,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object?>? parsedArguments = null)
    {
        Caller = caller;
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        RawInput = rawInput ?? throw new ArgumentNullException(nameof(rawInput));
        CancellationToken = cancellationToken;
        ParsedArguments = parsedArguments ?? new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    public PlayerId? Caller { get; }

    public IReadOnlyList<string> Arguments { get; }

    public string RawInput { get; }

    public CancellationToken CancellationToken { get; }

    public IReadOnlyDictionary<string, object?> ParsedArguments { get; }

    public T Get<T>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!ParsedArguments.TryGetValue(name, out var value))
        {
            throw new KeyNotFoundException($"Command argument '{name}' was not supplied.");
        }

        if (value is not T typed)
        {
            throw new InvalidCastException($"Command argument '{name}' is not a {typeof(T).Name}.");
        }

        return typed;
    }

    public bool TryGet<T>(string name, out T? value)
    {
        if (ParsedArguments.TryGetValue(name, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }
}
