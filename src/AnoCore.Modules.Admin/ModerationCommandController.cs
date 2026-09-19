using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;

namespace AnoCore.Modules.Admin;

public sealed class ModerationCommandController : IDisposable
{
    private static readonly ModuleId Owner = new("ano.admin");

    private readonly List<IDisposable> _registrations = [];
    private readonly ModerationCommandExecutor _executor;
    private int _disposed;

    public ModerationCommandController(
        IAnoCommandRegistry commands,
        ModerationCommandExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));

        Register(commands, ModerationAdminOperation.Ban, "anoban", "Ban a player.", requiresDuration: true);
        Register(commands, ModerationAdminOperation.Unban, "anounban", "Remove an active ban.", requiresDuration: false);
        Register(commands, ModerationAdminOperation.Mute, "anomute", "Mute a player's voice.", requiresDuration: true);
        Register(commands, ModerationAdminOperation.Unmute, "anounmute", "Remove an active voice mute.", requiresDuration: false);
        Register(commands, ModerationAdminOperation.Gag, "anogag", "Gag a player's text chat.", requiresDuration: true);
        Register(commands, ModerationAdminOperation.Ungag, "anoungag", "Remove an active chat gag.", requiresDuration: false);
        Register(commands, ModerationAdminOperation.Silence, "anosilence", "Mute voice and text chat.", requiresDuration: true);
        Register(commands, ModerationAdminOperation.Unsilence, "anounsilence", "Remove active voice and chat silence.", requiresDuration: false);
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

    private void Register(
        IAnoCommandRegistry commands,
        ModerationAdminOperation operation,
        string name,
        string description,
        bool requiresDuration)
    {
        IReadOnlyList<CommandArgumentDescriptor> arguments = requiresDuration
            ?
            [
                new CommandArgumentDescriptor(
                    "target",
                    CommandArgumentKind.String,
                    "Online name/SteamID64 or explicit offline SteamID64."),
                new CommandArgumentDescriptor(
                    "minutes",
                    CommandArgumentKind.Int32,
                    "Duration in minutes. Use 0 for permanent."),
                new CommandArgumentDescriptor(
                    "reason",
                    CommandArgumentKind.String,
                    "Optional moderation reason.",
                    required: false),
            ]
            :
            [
                new CommandArgumentDescriptor(
                    "target",
                    CommandArgumentKind.String,
                    "Online name/SteamID64 or explicit offline SteamID64."),
                new CommandArgumentDescriptor(
                    "reason",
                    CommandArgumentKind.String,
                    "Optional moderation reason.",
                    required: false),
            ];

        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                name,
                description,
                ModerationCommandExecutor.GetPermission(operation),
                arguments: arguments),
            context => ExecuteAsync(operation, requiresDuration, context)));
    }

    private ValueTask<CommandResult> ExecuteAsync(
        ModerationAdminOperation operation,
        bool requiresDuration,
        CommandContext context)
    {
        var target = context.Get<string>("target");
        var duration = requiresDuration ? context.Get<int>("minutes") : (int?)null;
        context.TryGet<string>("reason", out var reason);

        return _executor.ExecuteAsync(
            operation,
            context.Caller,
            target,
            duration,
            reason,
            context.CancellationToken);
    }
}
