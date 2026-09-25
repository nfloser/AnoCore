using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class RankModule : IDisposable
{
    private readonly RankConfiguration _configuration;
    private readonly IPlayerRegistry _players;
    private readonly ICombatRepository _combat;
    private readonly IDisposable _command;
    private int _disposed;

    private RankModule(RankConfiguration configuration, IAnoCommandRegistry commands,
        IPlayerRegistry players, ICombatRepository combat)
    {
        _configuration = configuration;
        _players = players;
        _combat = combat;
        _command = commands.Register(new ModuleId("ano.ranks"),
            new CommandDescriptor("anorank", "Show your combat rank and points."),
            context => ShowRankAsync(context.Caller, context.CancellationToken));
    }

    public static async Task<RankModule> CreateAsync(IConfigStore configuration,
        IAnoCommandRegistry commands, IPlayerRegistry players, ICombatRepository combat,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(combat);
        var settings = await configuration.LoadAsync("ranks",
            () => RankConfiguration.Default, RankConfiguration.Validate, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new RankModule(settings, commands, players, combat);
    }

    private async ValueTask<CommandResult> ShowRankAsync(PlayerId? caller,
        CancellationToken cancellationToken)
    {
        if (caller is null || !_players.TryGet(caller, out var player)
            || player is null || !player.IsConnected)
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "A connected player is required.");
        var totals = await _combat.ReadAsync(caller, cancellationToken).ConfigureAwait(false);
        var points = _configuration.Score(totals);
        var rank = _configuration.ForScore(points);
        return CommandResult.Ok($"[ANO] Rank: {rank.Name}; {points} point(s).");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _command.Dispose();
    }
}
