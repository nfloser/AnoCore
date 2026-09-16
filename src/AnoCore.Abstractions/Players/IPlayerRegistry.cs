namespace AnoCore.Abstractions.Players;

public interface IPlayerRegistry
{
    IReadOnlyCollection<PlayerSnapshot> OnlinePlayers { get; }

    bool TryGet(PlayerId id, out PlayerSnapshot? player);

    ValueTask<PlayerSnapshot> ConnectAsync(
        PlayerConnection connection,
        CancellationToken cancellationToken = default);

    ValueTask<PlayerSnapshot?> UpdateAsync(
        PlayerStateUpdate update,
        CancellationToken cancellationToken = default);

    ValueTask<PlayerSnapshot?> DisconnectAsync(
        PlayerId id,
        PlayerSessionId sessionId,
        DateTimeOffset disconnectedAtUtc,
        CancellationToken cancellationToken = default);
}
