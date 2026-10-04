using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;

namespace AnoCore.Modules.Admin;

public sealed class StatisticsResetCommandController : IDisposable
{
    private static readonly ModuleId Owner = new("ano.stats.admin");
    private readonly IDisposable _registration;
    private int _disposed;

    public StatisticsResetCommandController(
        IAnoCommandRegistry commands,
        StatisticsResetCommandExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(executor);

        _registration = commands.Register(
            Owner,
            new CommandDescriptor(
                "anoresetstats",
                "Reset one player's persisted statistics.",
                StatisticsResetCommandExecutor.Permission,
                arguments:
                [
                    new("target", CommandArgumentKind.String,
                        "Online name/SteamID64 or explicit offline SteamID64."),
                    new("reason", CommandArgumentKind.String,
                        "Optional administrative reason.", required: false),
                ]),
            context =>
            {
                context.TryGet<string>("reason", out var reason);
                return executor.ExecuteAsync(
                    context.Caller,
                    context.Get<string>("target"),
                    reason,
                    context.CancellationToken);
            });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _registration.Dispose();
    }
}
