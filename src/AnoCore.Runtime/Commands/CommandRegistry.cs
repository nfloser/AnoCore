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

        var context = new CommandContext(caller, tokens.Skip(1).ToArray(), input, cancellationToken);
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
