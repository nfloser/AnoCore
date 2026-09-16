using System.Globalization;
using System.Text;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;

namespace AnoCore.Runtime.Commands;

public sealed class CommandRegistry : IAnoCommandRegistry
{
    private readonly object _gate = new();
    private readonly IPermissionEvaluator _permissions;
    private readonly Dictionary<string, Registration> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<Registration, HashSet<string>> _registrationNames = [];

    public CommandRegistry(IPermissionEvaluator permissions)
        => _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));

    public IDisposable Register(ModuleId owner, CommandDescriptor descriptor, AnoCommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        ValidateAnoNamespace(descriptor.Name, nameof(descriptor));
        foreach (var alias in descriptor.Aliases)
        {
            ValidateAnoNamespace(alias, nameof(descriptor));
        }

        var registration = new Registration(owner, descriptor, handler);
        var names = new HashSet<string>([descriptor.Name, .. descriptor.Aliases], StringComparer.Ordinal);

        lock (_gate)
        {
            var collision = names.FirstOrDefault(_names.ContainsKey);
            if (collision is not null)
            {
                throw new InvalidOperationException($"Command or alias '{collision}' is already registered.");
            }

            foreach (var name in names)
            {
                _names.Add(name, registration);
            }

            _registrationNames.Add(registration, names);
        }

        return new RegistrationHandle(this, registration);
    }

    public void UnregisterAll(ModuleId owner)
    {
        lock (_gate)
        {
            foreach (var registration in _registrationNames.Keys.Where(registration => registration.Owner == owner).ToArray())
            {
                UnregisterUnsafe(registration);
            }
        }
    }

    public IReadOnlyCollection<CommandDescriptor> GetCommands()
    {
        lock (_gate)
        {
            return _registrationNames.Keys
                .Select(registration => registration.Descriptor)
                .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        string input,
        PlayerId? caller,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryTokenize(input, out var tokens, out var parseError) || tokens.Count == 0)
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, parseError ?? "A command is required.");
        }

        var commandName = NormalizeInvocation(tokens[0]);
        Registration? registration;
        lock (_gate)
        {
            _names.TryGetValue(commandName, out registration);
        }

        if (registration is null)
        {
            return CommandResult.Fail(CommandFailureReason.NotFound, $"Unknown command '{commandName}'.");
        }

        if (caller is { } playerId && registration.Descriptor.Permission is { } permission)
        {
            if (!await _permissions.HasPermissionAsync(playerId, permission, cancellationToken).ConfigureAwait(false))
            {
                return CommandResult.Fail(CommandFailureReason.Forbidden, "You are not allowed to use this command.");
            }
        }

        var rawArguments = tokens.Skip(1).ToArray();
        if (!TryParseArguments(registration.Descriptor, rawArguments, out var parsedArguments, out var argumentError))
        {
            return CommandResult.Fail(CommandFailureReason.InvalidInput, argumentError);
        }

        var context = new CommandContext(caller, rawArguments, input, cancellationToken, parsedArguments);
        try
        {
            return await registration.Handler(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return CommandResult.Fail(CommandFailureReason.HandlerFailed, exception.Message);
        }
    }

    private void Unregister(Registration registration)
    {
        lock (_gate)
        {
            UnregisterUnsafe(registration);
        }
    }

    private void UnregisterUnsafe(Registration registration)
    {
        if (!_registrationNames.Remove(registration, out var names))
        {
            return;
        }

        foreach (var name in names)
        {
            _names.Remove(name);
        }
    }

    private static string NormalizeInvocation(string token)
        => token.Trim().TrimStart('!', '/').ToLowerInvariant();

    private static void ValidateAnoNamespace(string name, string parameterName)
    {
        if (!name.StartsWith("ano", StringComparison.Ordinal))
        {
            throw new ArgumentException("AnoCore commands and aliases must use the 'ano' prefix.", parameterName);
        }
    }

    private static bool TryParseArguments(
        CommandDescriptor descriptor,
        IReadOnlyList<string> rawArguments,
        out IReadOnlyDictionary<string, object?> parsedArguments,
        out string? error)
    {
        var parsed = new Dictionary<string, object?>(StringComparer.Ordinal);
        parsedArguments = parsed;
        error = null;

        if (descriptor.Arguments.Count == 0)
        {
            return true;
        }

        var requiredCount = descriptor.Arguments.Count(argument => argument.Required);
        if (rawArguments.Count < requiredCount)
        {
            error = $"Usage: {descriptor.Usage}";
            return false;
        }

        if (rawArguments.Count > descriptor.Arguments.Count)
        {
            error = $"Usage: {descriptor.Usage}";
            return false;
        }

        for (var index = 0; index < rawArguments.Count; index++)
        {
            var specification = descriptor.Arguments[index];
            if (!TryParseValue(rawArguments[index], specification.Kind, out var value))
            {
                error = $"Invalid value for '{specification.Name}'. Usage: {descriptor.Usage}";
                return false;
            }

            parsed.Add(specification.Name, value);
        }

        return true;
    }

    private static bool TryParseValue(string raw, CommandArgumentKind kind, out object? value)
    {
        switch (kind)
        {
            case CommandArgumentKind.String:
                value = raw;
                return true;
            case CommandArgumentKind.Int32:
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                {
                    value = intValue;
                    return true;
                }

                break;
            case CommandArgumentKind.UInt64:
                if (ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ulongValue))
                {
                    value = ulongValue;
                    return true;
                }

                break;
            case CommandArgumentKind.Boolean:
                switch (raw.Trim().ToLowerInvariant())
                {
                    case "true":
                    case "1":
                    case "yes":
                    case "on":
                        value = true;
                        return true;
                    case "false":
                    case "0":
                    case "no":
                    case "off":
                        value = false;
                        return true;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported command argument kind.");
        }

        value = null;
        return false;
    }

    private static bool TryTokenize(string input, out List<string> tokens, out string? error)
    {
        tokens = [];
        error = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var current = new StringBuilder();
        var quoted = false;
        var escaped = false;

        foreach (var character in input.Trim())
        {
            if (escaped)
            {
                current.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\' && quoted)
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (escaped || quoted)
        {
            error = "The command contains an unterminated quoted argument.";
            tokens.Clear();
            return false;
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens.Count > 0;
    }

    private sealed record Registration(ModuleId Owner, CommandDescriptor Descriptor, AnoCommandHandler Handler);

    private sealed class RegistrationHandle : IDisposable
    {
        private CommandRegistry? _registry;
        private readonly Registration _registration;

        public RegistrationHandle(CommandRegistry registry, Registration registration)
        {
            _registry = registry;
            _registration = registration;
        }

        public void Dispose() => Interlocked.Exchange(ref _registry, null)?.Unregister(_registration);
    }
}
