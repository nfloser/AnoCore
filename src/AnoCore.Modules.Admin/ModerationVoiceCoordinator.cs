using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public enum ModerationVoiceOverride
{
    Default = 0,
    Mute = 1,
    Hear = 2,
}

public interface IModerationVoiceTransport
{
    bool TryGetOverride(
        PlayerSnapshot listener,
        PlayerSnapshot sender,
        out ModerationVoiceOverride value);

    bool TrySetOverride(
        PlayerSnapshot listener,
        PlayerSnapshot sender,
        ModerationVoiceOverride value);
}

public sealed class ModerationVoiceCoordinator : IDisposable
{
    private readonly IPlayerRegistry _players;
    private readonly ModerationVoiceGate _gate;
    private readonly IModerationVoiceTransport _transport;
    private readonly Dictionary<PairKey, OwnedOverride> _owned = [];
    private int _disposed;

    public ModerationVoiceCoordinator(
        IPlayerRegistry players,
        ModerationVoiceGate gate,
        IModerationVoiceTransport transport)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public void Reconcile()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

        var online = _players.OnlinePlayers
            .Where(player => player.IsConnected)
            .OrderBy(player => player.Id.SteamId64)
            .ThenBy(player => player.SessionId.Value)
            .ToArray();

        var currentPairs = new HashSet<PairKey>();

        foreach (var sender in online)
        {
            var blocked = _gate.Evaluate(sender.Id)
                == VoiceInterceptionDecision.Block;

            foreach (var listener in online)
            {
                if (SameSession(listener, sender))
                {
                    continue;
                }

                var key = PairKey.Of(listener, sender);
                currentPairs.Add(key);

                if (blocked)
                {
                    EnsureMuted(key, listener, sender);
                }
                else
                {
                    RestoreIfOwned(key, listener, sender);
                }
            }
        }

        foreach (var stale in _owned.Keys
                     .Where(key => !currentPairs.Contains(key))
                     .ToArray())
        {
            _owned.Remove(stale);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var online = _players.OnlinePlayers
            .Where(player => player.IsConnected)
            .ToDictionary(
                player => new EndpointKey(player.Id, player.SessionId),
                player => player);

        foreach (var owned in _owned.Values.ToArray())
        {
            if (!online.TryGetValue(
                    new EndpointKey(
                        owned.Listener.Id,
                        owned.Listener.SessionId),
                    out var listener)
                || !online.TryGetValue(
                    new EndpointKey(
                        owned.Sender.Id,
                        owned.Sender.SessionId),
                    out var sender))
            {
                continue;
            }

            _transport.TrySetOverride(
                listener,
                sender,
                owned.Original);
        }

        _owned.Clear();
    }

    private void EnsureMuted(
        PairKey key,
        PlayerSnapshot listener,
        PlayerSnapshot sender)
    {
        if (_owned.ContainsKey(key))
        {
            _transport.TrySetOverride(
                listener,
                sender,
                ModerationVoiceOverride.Mute);
            return;
        }

        if (!_transport.TryGetOverride(
                listener,
                sender,
                out var original))
        {
            return;
        }

        if (!_transport.TrySetOverride(
                listener,
                sender,
                ModerationVoiceOverride.Mute))
        {
            return;
        }

        _owned.Add(
            key,
            new OwnedOverride(listener, sender, original));
    }

    private void RestoreIfOwned(
        PairKey key,
        PlayerSnapshot listener,
        PlayerSnapshot sender)
    {
        if (!_owned.TryGetValue(key, out var owned))
        {
            return;
        }

        if (_transport.TrySetOverride(
                listener,
                sender,
                owned.Original))
        {
            _owned.Remove(key);
        }
    }

    private static bool SameSession(
        PlayerSnapshot left,
        PlayerSnapshot right)
        => left.Id == right.Id
            && left.SessionId == right.SessionId;

    private sealed record OwnedOverride(
        PlayerSnapshot Listener,
        PlayerSnapshot Sender,
        ModerationVoiceOverride Original);

    private sealed record EndpointKey(
        PlayerId PlayerId,
        PlayerSessionId SessionId);

    private sealed record PairKey(
        EndpointKey Listener,
        EndpointKey Sender)
    {
        public static PairKey Of(
            PlayerSnapshot listener,
            PlayerSnapshot sender)
            => new(
                new EndpointKey(listener.Id, listener.SessionId),
                new EndpointKey(sender.Id, sender.SessionId));
    }
}
