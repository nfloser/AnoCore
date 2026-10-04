using System.Globalization;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;

namespace AnoCore.Modules.Admin;

public sealed class ExtendedPositionCommandController : IDisposable
{
    private static readonly ModuleId Owner = new("ano.admin.extended.position");
    private readonly List<IDisposable> _registrations = [];
    private readonly ExtendedPositionCommandExecutor _executor;
    private int _disposed;

    public ExtendedPositionCommandController(
        IAnoCommandRegistry commands,
        ExtendedPositionCommandExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));

        try
        {
            RegisterTarget(
                commands,
                ExtendedPositionOperation.Respawn,
                "anorespawn",
                "Respawn a dead player.");
            RegisterTarget(
                commands,
                ExtendedPositionOperation.Revive,
                "anorevive",
                "Respawn a dead player at the recorded death position.");
            RegisterTeleportPosition(commands);
            RegisterTeleportPlayer(commands);
            RegisterTarget(
                commands,
                ExtendedPositionOperation.Bury,
                "anobury",
                "Move a living player down 25 world units.");
            RegisterTarget(
                commands,
                ExtendedPositionOperation.Unbury,
                "anounbury",
                "Move a living player up 30 world units.");
            RegisterSlap(commands);
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

    private void RegisterTarget(
        IAnoCommandRegistry commands,
        ExtendedPositionOperation operation,
        string name,
        string description)
    {
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                name,
                description,
                ExtendedPositionCommandExecutor.GetPermission(operation),
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "target",
                        CommandArgumentKind.String,
                        "One explicit online player."),
                ]),
            context => _executor.ExecuteAsync(
                operation,
                context.Caller,
                context.Get<string>("target"),
                [],
                context.CancellationToken)));
    }

    private void RegisterTeleportPosition(IAnoCommandRegistry commands)
    {
        var operation = ExtendedPositionOperation.TeleportPosition;
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anotppos",
                "Teleport a living player to world coordinates.",
                ExtendedPositionCommandExecutor.GetPermission(operation),
                aliases: ["anoteleportpos"],
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "target",
                        CommandArgumentKind.String,
                        "One explicit online player."),
                    new CommandArgumentDescriptor(
                        "x",
                        CommandArgumentKind.String,
                        "Finite world X coordinate."),
                    new CommandArgumentDescriptor(
                        "y",
                        CommandArgumentKind.String,
                        "Finite world Y coordinate."),
                    new CommandArgumentDescriptor(
                        "z",
                        CommandArgumentKind.String,
                        "Finite world Z coordinate."),
                ]),
            context => _executor.ExecuteAsync(
                operation,
                context.Caller,
                context.Get<string>("target"),
                [
                    context.Get<string>("x"),
                    context.Get<string>("y"),
                    context.Get<string>("z"),
                ],
                context.CancellationToken)));
    }

    private void RegisterTeleportPlayer(IAnoCommandRegistry commands)
    {
        var operation = ExtendedPositionOperation.TeleportPlayer;
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anotp",
                "Teleport a living player to another living player.",
                ExtendedPositionCommandExecutor.GetPermission(operation),
                aliases: ["anoteleport"],
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "target",
                        CommandArgumentKind.String,
                        "Player to move."),
                    new CommandArgumentDescriptor(
                        "destination",
                        CommandArgumentKind.String,
                        "Destination player."),
                ]),
            context => _executor.ExecuteAsync(
                operation,
                context.Caller,
                context.Get<string>("target"),
                [context.Get<string>("destination")],
                context.CancellationToken)));
    }

    private void RegisterSlap(IAnoCommandRegistry commands)
    {
        var operation = ExtendedPositionOperation.Slap;
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anoslap",
                "Slap a living player with optional damage.",
                ExtendedPositionCommandExecutor.GetPermission(operation),
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "target",
                        CommandArgumentKind.String,
                        "One explicit online player."),
                    new CommandArgumentDescriptor(
                        "damage",
                        CommandArgumentKind.Int32,
                        "Optional slap damage from 0 to 1000.",
                        required: false),
                ]),
            context =>
            {
                IReadOnlyList<string> arguments =
                    context.TryGet<int>("damage", out var damage)
                        ? [damage.ToString(CultureInfo.InvariantCulture)]
                        : [];

                return _executor.ExecuteAsync(
                    operation,
                    context.Caller,
                    context.Get<string>("target"),
                    arguments,
                    context.CancellationToken);
            }));
    }
}
