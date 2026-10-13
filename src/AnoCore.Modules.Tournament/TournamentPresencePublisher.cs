using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Tournament;

/// <summary>Publishes captured local identities; online status never implies readiness.</summary>
public sealed class TournamentPresencePublisher(ITournamentPresenceRepository repository, string serverId, Guid? instanceId = null) : IDisposable
{
    private readonly Guid _instanceId = instanceId ?? Guid.NewGuid();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

    public async ValueTask PublishAsync(IReadOnlyCollection<PlayerSnapshot> players, TournamentRecoverySnapshot? match,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        if (!await _gate.WaitAsync(0, linked.Token).ConfigureAwait(false)) return;
        try
        {
            var ready = match?.ReadyPlayers.ToHashSet() ?? [];
            var roster = players.Where(player => player.IsConnected).Select(player => new TournamentPlayerPresence(
                player.Id.SteamId64, player.SessionId.Value,
                DisplayName(player.Name),
                match?.MatchId, ready.Contains(player.Id))).ToArray();
            await repository.PublishAsync(serverId, _instanceId, roster, linked.Token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private static string DisplayName(string name)
    {
        var clean = new string(name.Where(c => !char.IsControl(c)).Take(128).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "Unknown" : clean;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        // In-flight operations own the linked cancellation source and gate release.
    }
}
