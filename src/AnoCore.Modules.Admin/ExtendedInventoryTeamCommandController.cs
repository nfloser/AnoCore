using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;

namespace AnoCore.Modules.Admin;

public sealed class ExtendedInventoryTeamCommandController : IDisposable
{
    private static readonly ModuleId Owner =
        new("ano.admin.extended.inventory-team");

    private readonly List<IDisposable> _registrations = [];
    private readonly ExtendedInventoryTeamCommandExecutor _executor;
    private readonly ITeamAdministrationCommandHandler? _teams;
    private int _disposed;

    public ExtendedInventoryTeamCommandController(
        IAnoCommandRegistry commands,
        ExtendedInventoryTeamCommandExecutor executor)
        : this(commands, executor, null)
    {
    }

    public ExtendedInventoryTeamCommandController(
        IAnoCommandRegistry commands,
        ExtendedInventoryTeamCommandExecutor executor,
        ITeamAdministrationCommandHandler? teams)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _teams = teams;

        try
        {
            RegisterRename(commands);
            RegisterTarget(
                commands,
                ExtendedInventoryTeamOperation.Strip,
                "anostrip",
                "Strip all weapons from a living player.");
            RegisterGive(commands);
            RegisterTeam(commands);
            RegisterTarget(
                commands,
                ExtendedInventoryTeamOperation.SwapTeam,
                "anoswap",
                "Swap a Terrorist or Counter-Terrorist to the opposite team.");
            RegisterHide(commands);
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
        _teams?.Dispose();
    }

    private void RegisterRename(IAnoCommandRegistry commands)
    {
        var operation = ExtendedInventoryTeamOperation.Rename;
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anorename",
                "Rename a connected player.",
                ExtendedInventoryTeamCommandExecutor.GetPermission(operation),
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "target",
                        CommandArgumentKind.String,
                        "One explicit online player."),
                    new CommandArgumentDescriptor(
                        "name",
                        CommandArgumentKind.String,
                        "New printable name; quote names containing spaces."),
                ]),
            context => _executor.ExecuteTargetAsync(
                operation,
                context.Caller,
                context.Get<string>("target"),
                [context.Get<string>("name")],
                context.CancellationToken)));
    }

    private void RegisterTarget(
        IAnoCommandRegistry commands,
        ExtendedInventoryTeamOperation operation,
        string name,
        string description)
    {
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                name,
                description,
                _teams is not null && operation == ExtendedInventoryTeamOperation.SwapTeam
                    ? null : ExtendedInventoryTeamCommandExecutor.GetPermission(operation),
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "target",
                        CommandArgumentKind.String,
                        "One explicit online player."),
                ]),
            context => _teams is not null && operation == ExtendedInventoryTeamOperation.SwapTeam
                ? _teams.ExecuteAsync(operation, context.Caller, context.Get<string>("target"), null, context.CancellationToken)
                : _executor.ExecuteTargetAsync(
                operation,
                context.Caller,
                context.Get<string>("target"),
                [],
                context.CancellationToken)));
    }

    private void RegisterGive(IAnoCommandRegistry commands)
    {
        var operation = ExtendedInventoryTeamOperation.Give;
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anogive",
                "Give a living player an approved item.",
                ExtendedInventoryTeamCommandExecutor.GetPermission(operation),
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "target",
                        CommandArgumentKind.String,
                        "One explicit online player."),
                    new CommandArgumentDescriptor(
                        "item",
                        CommandArgumentKind.String,
                        "Approved item class or exact alias."),
                ]),
            context => _executor.ExecuteTargetAsync(
                operation,
                context.Caller,
                context.Get<string>("target"),
                [context.Get<string>("item")],
                context.CancellationToken)));
    }

    private void RegisterTeam(IAnoCommandRegistry commands)
    {
        var operation = ExtendedInventoryTeamOperation.SetTeam;
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anoteam",
                "Move a player to Terrorist, Counter-Terrorist or Spectator.",
                _teams is null ? ExtendedInventoryTeamCommandExecutor.GetPermission(operation) : null,
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "target",
                        CommandArgumentKind.String,
                        "One explicit online player."),
                    new CommandArgumentDescriptor(
                        "team",
                        CommandArgumentKind.String,
                        "t, ct or spec."),
                ]),
            context => _teams is not null
                ? _teams.ExecuteAsync(operation, context.Caller, context.Get<string>("target"), context.Get<string>("team"), context.CancellationToken)
                : _executor.ExecuteTargetAsync(
                operation,
                context.Caller,
                context.Get<string>("target"),
                [context.Get<string>("team")],
                context.CancellationToken)));
    }

    private void RegisterHide(IAnoCommandRegistry commands)
    {
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anohide",
                "Hide your own connected player session.",
                ExtendedInventoryTeamCommandExecutor.GetHidePermission(),
                aliases: ["anostealth"]),
            context => _executor.ExecuteHideAsync(
                context.Caller,
                context.CancellationToken)));
    }
}
