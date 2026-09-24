using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;

namespace AnoCore.Modules.Admin;

public sealed class KickCommandController : IDisposable
{
    private static readonly ModuleId Owner = new("ano.admin.kick");
    private readonly List<IDisposable> _registrations = [];

    public KickCommandController(IAnoCommandRegistry commands, KickCommandExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(executor);
        try
        {
            Register(commands, executor, "anokick", false);
            Register(commands, executor, "anosilentkick", true);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        for (var i = _registrations.Count - 1; i >= 0; i--)
        {
            _registrations[i].Dispose();
        }

        _registrations.Clear();
    }

    private void Register(
        IAnoCommandRegistry commands,
        KickCommandExecutor executor,
        string name,
        bool silent)
    {
        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(
                name,
                silent ? "Kick a player without a public announcement." : "Kick a player.",
                KickCommandExecutor.GetPermission(silent),
                arguments:
                [
                    new CommandArgumentDescriptor(
                        "target", CommandArgumentKind.String, "One online name or SteamID64."),
                    new CommandArgumentDescriptor(
                        "reason", CommandArgumentKind.String, "Optional kick reason.", required: false),
                ]),
            context =>
            {
                context.TryGet<string>("reason", out var reason);
                return executor.ExecuteAsync(
                    silent, context.Caller, context.Get<string>("target"),
                    reason, context.CancellationToken);
            }));
    }
}
