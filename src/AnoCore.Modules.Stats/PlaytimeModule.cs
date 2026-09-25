using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Stats;

public sealed class PlaytimeModule : IDisposable
{
    private readonly IPlayerRegistry _players;
    private readonly IPlaytimeRepository _repository;
    private readonly List<IDisposable> _subscriptions = [];
    private int _disposed;

    private PlaytimeModule(IPlayerRegistry players, IPlaytimeRepository repository)
    {
        _players = players;
        _repository = repository;
    }

    public static async Task<PlaytimeModule> CreateAsync(IAnoEventBus events,
        IPlayerRegistry players, IPlaytimeRepository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(repository);
        var module = new PlaytimeModule(players, repository);
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
