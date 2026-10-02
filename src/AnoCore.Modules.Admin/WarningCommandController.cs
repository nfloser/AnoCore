using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;

namespace AnoCore.Modules.Admin;

public sealed class WarningCommandController : IDisposable
{
    private static readonly ModuleId Owner = new("ano.admin.warnings");
    private readonly List<IDisposable> _registrations = [];
    private readonly WarningCommandExecutor _executor;
    private int _disposed;

    public WarningCommandController(IAnoCommandRegistry commands, WarningCommandExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        try
        {
            _registrations.Add(commands.Register(Owner,
                new CommandDescriptor("anowarn", "Warn an online player.",
                    WarningCommandExecutor.WarnPermission, arguments:
                    [
                        new("target", CommandArgumentKind.String, "Online player."),
                        new("minutes", CommandArgumentKind.Int32, "Zero means permanent."),
                        new("reason", CommandArgumentKind.String, "Warning reason.", required: false),
                    ]),
                context =>
                {
                    context.TryGet<string>("reason", out var reason);
                    return _executor.WarnAsync(context.Caller, context.Get<string>("target"),
                        context.Get<int>("minutes"), reason, context.CancellationToken);
                }));
            _registrations.Add(commands.Register(Owner,
                new CommandDescriptor("anoclearwarns", "Clear active warnings.",
                    WarningCommandExecutor.ClearPermission, arguments:
                    [
                        new("target", CommandArgumentKind.String, "Online player."),
                        new("reason", CommandArgumentKind.String, "Clear reason.", required: false),
                    ]),
                context =>
                {
                    context.TryGet<string>("reason", out var reason);
                    return _executor.ClearAsync(context.Caller, context.Get<string>("target"),
                        reason, context.CancellationToken);
                }));
            _registrations.Add(commands.Register(Owner,
                new CommandDescriptor("anowarns", "Read a player's warning history.",
                    WarningCommandExecutor.ReadPermission, arguments:
                    [
                        new("target", CommandArgumentKind.String, "Player name or SteamID64."),
                    ]),
                context => _executor.TargetHistoryAsync(context.Caller,
                    context.Get<string>("target"), context.CancellationToken)));
            _registrations.Add(commands.Register(Owner,
                new CommandDescriptor("anomywarns", "Read your warning history."),
                context => _executor.OwnHistoryAsync(context.Caller, context.CancellationToken)));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        for (var index = _registrations.Count - 1; index >= 0; index--)
            _registrations[index].Dispose();
        _registrations.Clear();
    }
}
