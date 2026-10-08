using System.Globalization;
using System.Net;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class StatisticsMenuModule : IDisposable
{
    private const int PageSize = 5;
    private static readonly ModuleId Owner = new("ano.stats.menus");
    private readonly object _gate = new();
    private readonly IPlayerRegistry _players;
    private readonly IAnoCommandRegistry _commands;
    private readonly IMenuService _menus;
    private readonly IStatisticsMenuRepository _repository;
    private readonly Dictionary<PlayerId, Request> _requests = [];
    private readonly Dictionary<PlayerId, (PlayerSessionId Session, IDisposable Handle)> _registered = [];
    private readonly List<IDisposable> _registrations = [];
    private Func<int, int, CancellationToken, ValueTask<IReadOnlyList<CombatScoreRankEntry>>>? _rankTop;
    private Func<PlayerId, CancellationToken, ValueTask<CombatScoreRankEntry?>>? _rankPlacement;
    private int _generation;
    private bool _disposed;

    public StatisticsMenuModule(IAnoCommandRegistry commands, IPlayerRegistry players,
        IMenuService menus, IStatisticsMenuRepository repository, IAnoEventBus? events = null)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _menus = menus ?? throw new ArgumentNullException(nameof(menus));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        try
        {
            _registrations.Add(commands.Register(Owner, new("anostatsmenu", "Open server-wide statistics leaderboards."),
                context => ValueTask.FromResult(OpenCategories(context.Caller) ? CommandResult.Ok() : SessionChanged())));
            _registrations.Add(commands.Register(Owner, new("anopersonalstatsmenu", "Open your own persisted statistics."), OpenPersonalAsync));
            if (events is not null)
            {
                _registrations.Add(events.Subscribe<PlayerDisconnectedEvent>((value, _) => { Remove(value.Player); return ValueTask.CompletedTask; }));
                _registrations.Add(events.Subscribe<PlayerReconnectedEvent>((value, _) => { Remove(value.Previous); return ValueTask.CompletedTask; }));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void EnableRanks(RankConfiguration configuration, ICombatRepository combat)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(combat);
        RankScoreQueries.ValidateRepository(combat, configuration);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _rankTop = (limit, offset, token) => RankScoreQueries.TopAsync(combat, configuration, limit, offset, token);
            _rankPlacement = (player, token) => RankScoreQueries.PlacementAsync(combat, configuration, player, token);
        }
    }

    private bool OpenCategories(PlayerId? player)
    {
        var request = Begin(player);
        if (request is null) return false;
        var options = Enum.GetValues<StatisticsCategory>().Select(category => Option(Label(category),
            async selection => { await OpenLeaderboardAsync(request.Player.Id, category, false, 0, selection.CancellationToken).ConfigureAwait(false); })).ToList();
        if (_rankTop is not null)
            options.Add(Option("Rank points", async selection => { await OpenLeaderboardAsync(request.Player.Id, null, true, 0, selection.CancellationToken).ConfigureAwait(false); }));
        options.Add(new("home", "Home", async selection => { await _commands.ExecuteAsync("anomenu", selection.PlayerId, selection.CancellationToken).ConfigureAwait(false); }, keepOpen: true));
        return Replace(request, "Stats", options);
    }

    private async ValueTask<CommandResult> OpenPersonalAsync(CommandContext context)
    {
        var request = Begin(context.Caller);
        if (request is null) return SessionChanged();
        var snapshot = await _repository.ReadPersonalAsync(request.Player.Id, context.CancellationToken).ConfigureAwait(false);
        if (!Current(request)) return SessionChanged();
        CombatScoreRankEntry? rank = null;
        if (_rankPlacement is { } placement)
        {
            rank = await placement(request.Player.Id, context.CancellationToken).ConfigureAwait(false);
            if (!Current(request)) return SessionChanged();
        }
        var combat = snapshot.Combat;
        var options = new List<MenuOption>
        {
            Info($"Playtime: {Duration(snapshot.Playtime)}"),
            Info($"K/D/A: {combat.Kills}/{combat.Deaths}/{combat.Assists} · K/D: {(combat.Deaths > 0 ? (combat.Kills / (decimal)combat.Deaths).ToString("F2", CultureInfo.InvariantCulture) : "—")}"),
            Info($"HS: {Percentage(snapshot.Headshots, snapshot.NativeKills)} ({snapshot.NativeKills} native kills)"),
            Info($"Win: {Percentage(snapshot.MatchWins, snapshot.MatchWins + snapshot.MatchLosses)} ({snapshot.MatchWins}W/{snapshot.MatchLosses}L)"),
            Info($"Rounds won: {snapshot.RoundWins} · MVP: {snapshot.Mvp}"),
        };
        if (rank is not null) options.Add(Info($"Rank points: {rank.Points} · #{rank.Position}"));
        options.Add(new("back", "Stats", _ => { OpenCategories(request.Player.Id); return ValueTask.CompletedTask; }, keepOpen: true));
        return Replace(request, "Personal Stats", options) ? CommandResult.Ok() : SessionChanged();
    }

    private async ValueTask<bool> OpenLeaderboardAsync(PlayerId player, StatisticsCategory? category,
        bool rank, int page, CancellationToken token)
    {
        if (page is < 0 or > 1000) return false;
        var request = Begin(player);
        if (request is null) return false;
        IReadOnlyList<StatisticsRankEntry> entries;
        if (rank && _rankTop is { } rankTop)
        {
            var scores = await rankTop(PageSize + 1, page * PageSize, token).ConfigureAwait(false);
            entries = scores.Select(score => new StatisticsRankEntry(score.PlayerId, score.Points, score.Position, score.DisplayName)).ToArray();
        }
        else if (category is { } selected)
            entries = await _repository.GetTopAsync(selected, PageSize + 1, page * PageSize, token).ConfigureAwait(false);
        else return false;
        if (!Current(request)) return false;
        var label = rank ? "Rank points" : Label(category!.Value);
        var options = new List<MenuOption> { Info(label + (category is StatisticsCategory.KillDeathRatio ? " (≥50 kills, ≥20 deaths)"
            : category is StatisticsCategory.HeadshotPercentage ? " (≥50 native kills)"
            : category is StatisticsCategory.MatchWinPercentage ? " (≥10 matches)" : "")) };
        options.AddRange(entries.Take(PageSize).Select(entry => Info($"#{entry.Position} {Name(entry)}{(entry.PlayerId == player ? " (you)" : "")}: {Value(category, entry.Value)}")));
        if (entries.Count == 0) options.Add(Info("No recorded players meet this category's requirements."));
        if (page > 0)
            options.Add(Option("Previous page", async selection => { await OpenLeaderboardAsync(player, category, rank, page - 1, selection.CancellationToken).ConfigureAwait(false); }));
        if (entries.Count > PageSize && page < 1000)
            options.Add(Option("Next page", async selection => { await OpenLeaderboardAsync(player, category, rank, page + 1, selection.CancellationToken).ConfigureAwait(false); }));
        options.Add(new("back", "Categories", _ => { OpenCategories(player); return ValueTask.CompletedTask; }, keepOpen: true));
        return Replace(request, "Stats", options);
    }

    private Request? Begin(PlayerId? id)
    {
        lock (_gate)
        {
            if (_disposed || id is null || !_players.TryGet(id, out var player) || player is not { IsConnected: true }) return null;
            _menus.TryGetOpenMenu(id, out var expected);
            var request = new Request(player, expected);
            _requests[id] = request;
            return request;
        }
    }

    private bool Current(Request request)
    {
        lock (_gate)
        {
            if (_disposed || !_requests.TryGetValue(request.Player.Id, out var current) || !ReferenceEquals(current, request)
                || !_players.TryGet(request.Player.Id, out var player) || player is not { IsConnected: true }
                || player.SessionId != request.Player.SessionId) return false;
            _menus.TryGetOpenMenu(request.Player.Id, out var menu);
            return ReferenceEquals(menu, request.Expected);
        }
    }

    private bool Replace(Request request, string title, IReadOnlyCollection<MenuOption> options)
    {
        lock (_gate)
        {
            if (!Current(request)) return false;
            if (_registered.Remove(request.Player.Id, out var old)) old.Handle.Dispose();
            var definition = new MenuDefinition(new($"ano.statistics.{request.Player.Id.SteamId64}"), title, options) { SuppressPageIndicator = true };
            var handle = _menus.Register(Owner, definition);
            _registered[request.Player.Id] = (request.Player.SessionId, handle);
            request.Expected = definition;
            _menus.Open(request.Player.Id, definition.Id);
            return true;
        }
    }

    private void Remove(PlayerSnapshot player)
    {
        lock (_gate)
        {
            if (_requests.TryGetValue(player.Id, out var request) && request.Player.SessionId == player.SessionId) _requests.Remove(player.Id);
            if (_registered.TryGetValue(player.Id, out var registration) && registration.Session == player.SessionId)
            {
                _registered.Remove(player.Id);
                registration.Handle.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var registration in _registered.Values) registration.Handle.Dispose();
            _registered.Clear();
            _requests.Clear();
        }
        foreach (var registration in _registrations) registration.Dispose();
        _registrations.Clear();
    }

    private MenuOption Option(string label, Func<MenuSelectionContext, ValueTask> handler)
        => new($"s{Interlocked.Increment(ref _generation):x8}", label, handler, keepOpen: true);
    private MenuOption Info(string label) => Option(label, static _ => ValueTask.CompletedTask);
    private static string Name(StatisticsRankEntry entry) => WebUtility.HtmlEncode(new string((string.IsNullOrWhiteSpace(entry.DisplayName)
        ? entry.PlayerId.ToString() : entry.DisplayName).Where(character => !char.IsControl(character)).Take(64).ToArray()));
    private static string Percentage(long numerator, long denominator) => denominator > 0
        ? (100m * numerator / denominator).ToString("F1", CultureInfo.InvariantCulture) + "%" : "—";
    private static string Duration(TimeSpan duration) => $"{(long)duration.TotalHours}h {duration.Minutes}m";
    private static string Value(StatisticsCategory? category, decimal value) => category switch
    {
        StatisticsCategory.Playtime => Duration(TimeSpan.FromSeconds((double)value)),
        StatisticsCategory.KillDeathRatio => value.ToString("F2", CultureInfo.InvariantCulture),
        StatisticsCategory.HeadshotPercentage or StatisticsCategory.MatchWinPercentage => value.ToString("F1", CultureInfo.InvariantCulture) + "%",
        _ => value.ToString("0", CultureInfo.InvariantCulture),
    };
    private static string Label(StatisticsCategory category) => category switch
    {
        StatisticsCategory.Playtime => "Playtime",
        StatisticsCategory.Kills => "Kills",
        StatisticsCategory.Deaths => "Deaths",
        StatisticsCategory.Assists => "Assists",
        StatisticsCategory.KillDeathRatio => "K/D",
        StatisticsCategory.HeadshotPercentage => "HS%",
        StatisticsCategory.MatchWins => "Match wins",
        StatisticsCategory.MatchWinPercentage => "Match win%",
        StatisticsCategory.RoundWins => "Round wins",
        StatisticsCategory.Mvp => "MVP",
        StatisticsCategory.BombPlants => "Bomb plants",
        StatisticsCategory.BombDefuses => "Bomb defuses",
        StatisticsCategory.GrenadeKills => "Grenade kills",
        StatisticsCategory.KnifeKills => "Knife kills",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };
    private static CommandResult SessionChanged() => CommandResult.Fail(CommandFailureReason.InvalidInput, "Statistics menu or player session changed while loading.");
    private sealed class Request(PlayerSnapshot player, MenuDefinition? expected)
    {
        public PlayerSnapshot Player { get; } = player;
        public MenuDefinition? Expected { get; set; } = expected;
    }
}
