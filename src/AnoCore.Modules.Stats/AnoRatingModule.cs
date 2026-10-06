using System.Globalization;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class AnoRatingModule : IDisposable
{
    private const int PageSize = 5;
    private readonly IPlayerRegistry _players;
    private readonly ICombatRepository _combat;
    private readonly IGameplayStatRepository _gameplay;
    private readonly IDisposable _command;
    private int _disposed;

    public AnoRatingModule(IAnoCommandRegistry commands, IPlayerRegistry players,
        ICombatRepository combat, IGameplayStatRepository gameplay)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _gameplay = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
        _command = commands.Register(new ModuleId("ano.rating"),
            new CommandDescriptor("anorating", "Inspect internal ratings for manual team composition.",
                arguments:
                [
                    new("player", CommandArgumentKind.String, "Player name, SteamID64, or list.", required: false),
                    new("page", CommandArgumentKind.Int32, "List page (1-26).", required: false),
                ]), ExecuteAsync);
    }

    private async ValueTask<CommandResult> ExecuteAsync(CommandContext context)
    {
        if (Volatile.Read(ref _disposed) != 0) return Unavailable();
        PlayerSnapshot? caller = null;
        if (context.Caller is not null && (!_players.TryGet(context.Caller, out caller)
                || caller is not { IsConnected: true }))
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "A connected caller is required.");
        var query = context.Arguments.Count == 0 ? "list" : context.Arguments[0];
        if (query.Length is < 1 or > 128 || query.Any(char.IsControl))
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "Invalid player selector.");
        var list = string.Equals(query, "list", StringComparison.OrdinalIgnoreCase);
        var page = 1;
        if (context.Arguments.Count > 1 && (!list || !int.TryParse(context.Arguments[1],
                NumberStyles.None, CultureInfo.InvariantCulture, out page) || page is < 1 or > 26))
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "Use !anorating list <page 1-26>.");
        // The native registry contains connected humans only. Bound database work even for other adapters.
        var online = _players.OnlinePlayers.Where(player => player.IsConnected)
            .OrderBy(player => player.Id.SteamId64).ToArray();
        if (online.Length > 128)
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "Rating supports at most 128 connected players.");
        var targets = online;
        if (!list)
        {
            if (ulong.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out var steam))
                targets = online.Where(player => player.Id.SteamId64 == steam).ToArray();
            else
            {
                targets = online.Where(player => string.Equals(player.Name, query,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
                if (targets.Length == 0)
                    targets = online.Where(player => player.Name.Contains(query,
                        StringComparison.OrdinalIgnoreCase)).ToArray();
            }
            if (targets.Length != 1)
                return CommandResult.Fail(CommandFailureReason.InvalidInput,
                    targets.Length == 0 ? "No connected player matches." : "Player selector is ambiguous; use SteamID64.");
        }
        var results = new List<(PlayerSnapshot Player, AnoRatingResult Rating)>();
        foreach (var target in targets)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var combat = await _combat.ReadAsync(target.Id, context.CancellationToken).ConfigureAwait(false);
            if (!Current(target) || !CallerCurrent(caller))
            {
                if (!list || !CallerCurrent(caller)) return SessionChanged();
                continue;
            }
            CombatDetailTotals? detail = null;
            if (_combat is ICombatDetailRepository details)
                detail = await details.ReadDetailsAsync(target.Id,
                    cancellationToken: context.CancellationToken).ConfigureAwait(false);
            if (!Current(target) || !CallerCurrent(caller))
            {
                if (!list || !CallerCurrent(caller)) return SessionChanged();
                continue;
            }
            var gameplay = await _gameplay.ReadAsync(target.Id,
                cancellationToken: context.CancellationToken).ConfigureAwait(false);
            if (!Current(target) || !CallerCurrent(caller))
            {
                if (!list || !CallerCurrent(caller)) return SessionChanged();
                continue;
            }
            results.Add((target, AnoRatingCalculator.Calculate(combat, detail, gameplay)));
        }
        if (Volatile.Read(ref _disposed) != 0) return Unavailable();
        if (!CallerCurrent(caller)) return SessionChanged();
        results.RemoveAll(entry => !Current(entry.Player));
        if (!list)
        {
            if (results.Count != 1) return SessionChanged();
            var entry = results[0];
            var dimensions = string.Join(", ", entry.Rating.Dimensions.Select(dimension =>
                $"{dimension.Name}={dimension.Value.ToString("F0", CultureInfo.InvariantCulture)}/100"));
            return CommandResult.Ok($"[ANO] {Name(entry.Player)}: {Display(entry.Rating)}; "
                + $"{entry.Rating.Rounds} rounds; K/D/A={entry.Rating.Combat.Kills}/"
                + $"{entry.Rating.Combat.Deaths}/{entry.Rating.Combat.Assists}; "
                + $"{dimensions}; {entry.Rating.AlgorithmVersion}. Observational estimate.");
        }
        var ordered = results.OrderByDescending(entry => entry.Rating.Score ?? -1)
            .ThenBy(entry => entry.Player.Id.SteamId64).ToArray();
        var entries = ordered.Skip((page - 1) * PageSize).Take(PageSize).ToArray();
        if (entries.Length == 0) return CommandResult.Ok("[ANO] No rating entries on this page.");
        return CommandResult.Ok($"[ANO] AnoRating {page}/{(ordered.Length + PageSize - 1) / PageSize}: "
            + string.Join(" | ", entries.Select(entry => $"{Name(entry.Player)}: {Display(entry.Rating)}"))
            + ". Details: !anorating <player>; pages: !anorating list <page>.");
    }

    private bool CallerCurrent(PlayerSnapshot? caller) => caller is null || Current(caller);
    private bool Current(PlayerSnapshot player)
        => Volatile.Read(ref _disposed) == 0 && _players.TryGet(player.Id, out var current)
            && current is { IsConnected: true } && current.SessionId == player.SessionId;
    private static string Name(PlayerSnapshot player)
        => new(player.Name.Where(character => !char.IsControl(character)
            && character is not '{' and not '}' and not '|').Take(24).ToArray());
    private static string Display(AnoRatingResult rating)
        => $"{(rating.Score is null ? "unscored" : rating.Score.Value.ToString(CultureInfo.InvariantCulture) + "/1000")} "
            + $"({rating.Confidence.ToString().ToLowerInvariant()})";
    private static CommandResult SessionChanged()
        => CommandResult.Fail(CommandFailureReason.InvalidInput, "Player session changed while ratings were loading.");
    private static CommandResult Unavailable()
        => CommandResult.Fail(CommandFailureReason.NotFound, "AnoRating is no longer available.");

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _command.Dispose();
    }
}
