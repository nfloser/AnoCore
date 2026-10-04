using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;

namespace AnoCore.Modules.Admin;

public sealed class ProtectedServerControlCommandController : IDisposable
{
    private static readonly ModuleId Owner =
        new("ano.admin.extended.server-controls");

    private readonly List<IDisposable> _registrations = [];
    private readonly ProtectedServerControlExecutor _executor;
    private int _disposed;

    public ProtectedServerControlCommandController(
        IAnoCommandRegistry commands,
        ProtectedServerControlExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));

        try
        {
            _registrations.Add(commands.Register(
                Owner,
                new CommandDescriptor(
                    "anocvar",
                    "Set an explicitly allowed server ConVar.",
                    ProtectedServerControlExecutor.GetCVarPermission(),
                    arguments:
                    [
                        new CommandArgumentDescriptor(
                            "name",
                            CommandArgumentKind.String,
                            "Allowed ConVar name."),
                        new CommandArgumentDescriptor(
                            "value",
                            CommandArgumentKind.String,
                            "Value; quote values containing spaces."),
                    ]),
                context => _executor.SetCVarAsync(
                    context.Caller,
                    context.Get<string>("name"),
                    context.Get<string>("value"),
                    context.CancellationToken)));

            _registrations.Add(commands.Register(
                Owner,
                new CommandDescriptor(
                    "anoserver",
                    "Execute an explicitly allowed server command.",
                    ProtectedServerControlExecutor.GetServerCommandPermission(),
                    arguments:
                    [
                        new CommandArgumentDescriptor(
                            "command",
                            CommandArgumentKind.String,
                            "Allowed server command name."),
                        new CommandArgumentDescriptor(
                            "arguments",
                            CommandArgumentKind.String,
                            "Optional command arguments; quote spaces.",
                            required: false),
                    ]),
                context =>
                {
                    context.TryGet<string>("arguments", out var arguments);
                    return _executor.ExecuteServerCommandAsync(
                        context.Caller,
                        context.Get<string>("command"),
                        arguments,
                        context.CancellationToken);
                }));

            _registrations.Add(commands.Register(
                Owner,
                new CommandDescriptor(
                    "anosameip",
                    "List connected players sharing a normalized network address without exposing raw IPs.",
                    ProtectedServerControlExecutor.GetSameIpPermission(),
                    aliases: ["anoantighosting"]),
                context => _executor.ListSameIpAsync(
                    context.Caller,
                    context.CancellationToken)));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        for (var index = _registrations.Count - 1; index >= 0; index--)
        {
            _registrations[index].Dispose();
        }

        _registrations.Clear();
    }
}
