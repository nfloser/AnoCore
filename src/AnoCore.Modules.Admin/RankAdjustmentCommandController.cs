using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Admin;

public sealed class RankAdjustmentCommandController : IDisposable
{
    private static readonly ModuleId Owner = new("ano.ranks.admin");
    private readonly List<IDisposable> _registrations = [];
    private readonly RankAdjustmentCommandExecutor _executor;
    private int _disposed;

    public RankAdjustmentCommandController(
        IAnoCommandRegistry commands,
        RankAdjustmentCommandExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        try
        {
            Register(commands, RankAdjustmentAdminOperation.Give,
                "anogiverankpoints", "Give rank points.", requiresPoints: true);
            Register(commands, RankAdjustmentAdminOperation.Take,
                "anotakerankpoints", "Take rank points.", requiresPoints: true);
            Register(commands, RankAdjustmentAdminOperation.Set,
                "anosetrankpoints", "Set a rank point adjustment.", requiresPoints: true);
            Register(commands, RankAdjustmentAdminOperation.Reset,
                "anoresetrankpoints", "Reset a rank point adjustment.", requiresPoints: false);
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

    private void Register(IAnoCommandRegistry commands,
        RankAdjustmentAdminOperation operation, string name, string description,
        bool requiresPoints)
    {
        IReadOnlyList<CommandArgumentDescriptor> arguments = requiresPoints
            ? [
                new("target", CommandArgumentKind.String,
                    "Online name/SteamID64 or explicit offline SteamID64."),
                new("points", CommandArgumentKind.Int32, "Point amount."),
                new("reason", CommandArgumentKind.String,
                    "Optional administrative reason.", required: false),
            ]
            : [
                new("target", CommandArgumentKind.String,
                    "Online name/SteamID64 or explicit offline SteamID64."),
                new("reason", CommandArgumentKind.String,
                    "Optional administrative reason.", required: false),
            ];

        _registrations.Add(commands.Register(
            Owner,
            new CommandDescriptor(name, description,
                RankAdjustmentCommandExecutor.GetPermission(operation),
                arguments: arguments),
            context =>
            {
                var points = requiresPoints ? context.Get<int>("points") : 0;
                context.TryGet<string>("reason", out var reason);
                return _executor.ExecuteAsync(operation, context.Caller,
                    context.Get<string>("target"), points, reason,
                    context.CancellationToken);
            }));
    }
}
