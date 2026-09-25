using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class PlaytimeModule : IDisposable
{
    private readonly IPlayerRegistry _players;
    private readonly IPlaytimeRepository _repository;
    private readonly TimeProvider _clock;
    private readonly List<IDisposable> _subscriptions = [];
    private int _disposed;

    private PlaytimeModule(IPlayerRegistry players, IPlaytimeRepository repository, TimeProvider clock)
    {
        _players = players;
        _repository = repository;
        _clock = clock;
    }

    public static async Task<PlaytimeModule> CreateAsync(IAnoEventBus events,
        IPlayerRegistry players, IPlaytimeRepository repository,
        IAnoCommandRegistry commands, TimeProvider? clock = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(commands);
        var module = new PlaytimeModule(players, repository, clock ?? TimeProvider.System);
        try
        {
            module._subscriptions.Add(events.Subscribe<PlayerConnectedEvent>(
                (value, token) => module.OpenAsync(value.Player, token)));
            module._subscriptions.Add(events.Subscribe<PlayerReconnectedEvent>(async (value, token) =>
            {
                await module.RecordAsync(value.Previous, value.Current.ConnectedAtUtc, true, token)
                    .ConfigureAwait(false);
                await module.OpenAsync(value.Current, token).ConfigureAwait(false);
            }));
            module._subscriptions.Add(events.Subscribe<PlayerDisconnectedEvent>(
                (value, token) => module.RecordAsync(
                    value.Player, value.Player.LastUpdatedAtUtc, true, token)));
            module._subscriptions.Add(commands.Register(new ModuleId("ano.stats"),
                new CommandDescriptor("anoplaytime", "Show your total and today's UTC playtime."),
                context => module.OwnPlaytimeAsync(context.Caller, context.CancellationToken)));
            module._subscriptions.Add(commands.Register(new ModuleId("ano.stats"),
                new CommandDescriptor("anotoptime", "Show the playtime leaderboard.", arguments:
                [
                    new("page", CommandArgumentKind.Int32, "Page number.", required: false),
                ]),
                context => module.TopTimeAsync(context)));
            foreach (var player in players.OnlinePlayers)
                await module.OpenAsync(player, cancellationToken).ConfigureAwait(false);
            return module;
        }
        catch
        {
            module.Dispose();
            throw;
        }
    }

    public async ValueTask CheckpointOnlineAsync(DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        foreach (var player in _players.OnlinePlayers)
            await RecordAsync(player, atUtc, false, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<CommandResult> OwnPlaytimeAsync(PlayerId? caller,
        CancellationToken cancellationToken)
    {
        if (caller is null || !_players.TryGet(caller, out var player)
            || player is null || !player.IsConnected)
            return CommandResult.Fail(CommandFailureReason.InvalidInput, "A connected player is required.");
        var now = _clock.GetUtcNow();
        await RecordAsync(player, now, false, cancellationToken).ConfigureAwait(false);
        var totals = await _repository.ReadAsync(caller,
            DateOnly.FromDateTime(now.UtcDateTime), cancellationToken).ConfigureAwait(false);
        return CommandResult.Ok($"[ANO] Playtime: {totals.Total:c}; today (UTC): {totals.Today:c}.");
    }

    private async ValueTask<CommandResult> TopTimeAsync(CommandContext context)
    {
        var page = context.ParsedArguments.TryGetValue("page", out var provided)
            ? (int)provided! : 1;
        if (page is < 1 or > 1000)
            return CommandResult.Fail(CommandFailureReason.InvalidInput,
                "Page must be between 1 and 1000.");
        const int pageSize = 5;
        var entries = await _repository.GetTopAsync(pageSize, (page - 1) * pageSize,
            context.CancellationToken).ConfigureAwait(false);
        if (entries.Count == 0) return CommandResult.Ok("No playtime entries on this page.");
        return CommandResult.Ok(string.Join(" | ", entries.Select(entry =>
            $"{entry.Position}. {Display(entry)}: {entry.Total:c}")));
    }

    private static string Display(PlaytimeRankEntry entry)
        => string.IsNullOrWhiteSpace(entry.DisplayName)
            ? entry.PlayerId.SteamId64.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{entry.DisplayName.Replace('\\r', ' ').Replace('\\n', ' ').Replace('|', '/')} "
                + $"({entry.PlayerId.SteamId64})";

    private ValueTask OpenAsync(PlayerSnapshot player, CancellationToken cancellationToken)
        => _repository.OpenAsync(player.Id, player.SessionId, player.ConnectedAtUtc, cancellationToken);

    private async ValueTask RecordAsync(PlayerSnapshot player, DateTimeOffset atUtc, bool close,
        CancellationToken cancellationToken)
    {
        await OpenAsync(player, cancellationToken).ConfigureAwait(false);
        await _repository.AdvanceAsync(player.Id, player.SessionId, atUtc, close, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        for (var i = _subscriptions.Count - 1; i >= 0; i--)
            _subscriptions[i].Dispose();
        _subscriptions.Clear();
    }
}
