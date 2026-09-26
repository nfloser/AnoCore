using System.Globalization;
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
    private readonly IDisposable _topCommand;
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
        try
        {
            _topCommand = commands.Register(new ModuleId("ano.ranks"),
                new CommandDescriptor("anotopranks", "Show the combat rank leaderboard.", arguments:
                [new("page", CommandArgumentKind.Int32, "Page number.", required: false)]),
                ShowTopRanksAsync);
        }
        catch
        {
            _command.Dispose();
            throw;
        }
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
        var placement = await _combat.GetScorePlacementAsync(caller,
            _configuration.KillPoints, _configuration.AssistPoints,
            _configuration.DeathPenalty, cancellationToken).ConfigureAwait(false);
        var points = placement?.Points ?? 0;
        var rank = _configuration.ForScore(points);
        var position = placement is null ? " Unranked." : $" Placement: #{placement.Position}.";
        var next = _configuration.NextAfter(points);
        var progress = next is null
            ? " Highest configured rank reached."
            : $" {next.MinimumPoints - points} point(s) to {next.Name}.";
        return CommandResult.Ok(
            $"[ANO] Rank: {rank.Name}; {points} point(s).{position}{progress}");
    }

    private async ValueTask<CommandResult> ShowTopRanksAsync(CommandContext context)
    {
        var page = context.ParsedArguments.TryGetValue("page", out var provided)
            ? (int)provided! : 1;
        if (page is < 1 or > 1000)
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "Page must be between 1 and 1000.");
        const int pageSize = 5;
        var entries = await _combat.GetTopScoresAsync(
            _configuration.KillPoints, _configuration.AssistPoints,
            _configuration.DeathPenalty, pageSize, (page - 1) * pageSize,
            context.CancellationToken).ConfigureAwait(false);
        if (entries.Count == 0) return CommandResult.Ok("No rank entries on this page.");
        return CommandResult.Ok(string.Join(" | ", entries.Select(entry =>
        {
            var rank = _configuration.ForScore(entry.Points);
            return $"{entry.Position}. {Display(entry)}: {rank.Name}, {entry.Points} point(s)";
        })));
    }

    private static string Display(CombatScoreRankEntry entry)
        => string.IsNullOrWhiteSpace(entry.DisplayName)
            ? entry.PlayerId.SteamId64.ToString(CultureInfo.InvariantCulture)
            : $"{entry.DisplayName.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/')} "
                + $"({entry.PlayerId.SteamId64})";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _topCommand.Dispose();
        _command.Dispose();
    }
}
