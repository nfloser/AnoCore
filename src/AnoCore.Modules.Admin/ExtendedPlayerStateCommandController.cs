using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;

namespace AnoCore.Modules.Admin;

public sealed class ExtendedPlayerStateCommandController : IDisposable
{
    private static readonly ModuleId Owner = new("ano.admin.extended");
    private readonly List<IDisposable> _registrations = [];
    private readonly ExtendedPlayerStateCommandExecutor _executor;
    private int _disposed;

    public ExtendedPlayerStateCommandController(
        IAnoCommandRegistry commands,
        ExtendedPlayerStateCommandExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));

        try
        {
            RegisterValue(commands, ExtendedPlayerStateOperation.SetHealth,
                "anohealth", "Set a living player's health.", "value");
            RegisterValue(commands, ExtendedPlayerStateOperation.SetArmor,
                "anoarmor", "Set a living player's armor.", "value");
            RegisterTarget(commands, ExtendedPlayerStateOperation.Freeze,
                "anofreeze", "Freeze a living player.");
            RegisterTarget(commands, ExtendedPlayerStateOperation.Unfreeze,
                "anounfreeze", "Restore AnoCore-owned movement state.");
            RegisterTarget(commands, ExtendedPlayerStateOperation.Noclip,
                "anonoclip", "Enable noclip for a living player.");
            RegisterTarget(commands, ExtendedPlayerStateOperation.Walk,
                "anowalk", "Restore AnoCore-owned normal movement.", ["anoclipoff"]);
            RegisterTarget(commands, ExtendedPlayerStateOperation.Slay,
                "anoslay", "Slay a living player.");
            RegisterValue(commands, ExtendedPlayerStateOperation.SetSpeed,
                "anospeed", "Set a living player's speed percentage.", "percent");
            RegisterTarget(commands, ExtendedPlayerStateOperation.ResetSpeed,
                "anoresetspeed", "Restore AnoCore-owned speed.");
            RegisterBlind(commands);
            RegisterTarget(commands, ExtendedPlayerStateOperation.Unblind,
                "anounblind", "Restore AnoCore-owned blindness state.");
            RegisterTarget(commands, ExtendedPlayerStateOperation.God,
                "anogod", "Disable damage for a living player.");
            RegisterTarget(commands, ExtendedPlayerStateOperation.Ungod,
                "anoungod", "Restore AnoCore-owned damage handling.");
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
        ExtendedPlayerStateOperation operation,
        string name,
        string description,
        IEnumerable<string>? aliases = null)
    {
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                name,
                description,
                ExtendedPlayerStateCommandExecutor.GetPermission(operation),
                aliases: aliases,
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "target",
                        CommandArgumentKind.String,
                        "One online player name or SteamID64."),
                ]),
            context => _executor.ExecuteAsync(
                operation,
                context.Caller,
                context.Get<string>("target"),
                null,
                context.CancellationToken)));
    }

    private void RegisterValue(
        IAnoCommandRegistry commands,
        ExtendedPlayerStateOperation operation,
        string name,
        string description,
        string valueName)
    {
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                name,
                description,
                ExtendedPlayerStateCommandExecutor.GetPermission(operation),
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "target",
                        CommandArgumentKind.String,
                        "One online player name or SteamID64."),
                    new CommandArgumentDescriptor(
                        valueName,
                        CommandArgumentKind.Int32,
                        $"Numeric {valueName}."),
                ]),
            context => _executor.ExecuteAsync(
                operation,
                context.Caller,
                context.Get<string>("target"),
                context.Get<int>(valueName),
                context.CancellationToken)));
    }

    private void RegisterBlind(IAnoCommandRegistry commands)
    {
        var operation = ExtendedPlayerStateOperation.Blind;
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                "anoblind",
                "Blind a living player.",
                ExtendedPlayerStateCommandExecutor.GetPermission(operation),
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "target",
                        CommandArgumentKind.String,
                        "One online player name or SteamID64."),
                    new CommandArgumentDescriptor(
                        "alpha",
                        CommandArgumentKind.Int32,
                        "Optional flash alpha from 0 to 255.",
                        required: false),
                ]),
            context =>
            {
                context.TryGet<int>("alpha", out var alpha);
                return _executor.ExecuteAsync(
                    operation,
                    context.Caller,
                    context.Get<string>("target"),
                    context.ParsedArguments.ContainsKey("alpha") ? alpha : null,
                    context.CancellationToken);
            }));
    }
}
