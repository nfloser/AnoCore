using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public enum ExtendedPositionOperation
{
    Respawn = 1,
    Revive = 2,
    TeleportPosition = 3,
    TeleportPlayer = 4,
    Bury = 5,
    Unbury = 6,
    Slap = 7,
}

public readonly record struct PlayerWorldPosition
{
    public const float CoordinateLimit = 32768f;

    public PlayerWorldPosition(float x, float y, float z)
    {
        Validate(x, nameof(x));
        Validate(y, nameof(y));
        Validate(z, nameof(z));
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    public PlayerWorldPosition OffsetZ(float offset)
        => new(X, Y, checked(Z + offset));

    private static void Validate(float value, string parameterName)
    {
        if (!float.IsFinite(value) || Math.Abs(value) > CoordinateLimit)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"World coordinates must be finite and within ±{CoordinateLimit}.");
        }
    }
}

public interface IExtendedPositionTransport
{
    ValueTask<PlayerWorldPosition> ReadPositionAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default);

    ValueTask RespawnAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default);

    ValueTask TeleportAsync(
        PlayerSnapshot player,
        PlayerWorldPosition position,
        CancellationToken cancellationToken = default);

    ValueTask SlapAsync(
        PlayerSnapshot player,
        int damage,
        CancellationToken cancellationToken = default);
}

public sealed class ExtendedPositionService
{
    private readonly object _gate = new();
    private readonly Dictionary<PlayerSessionId, PlayerWorldPosition> _deathPositions = [];
    private readonly IExtendedPositionTransport _transport;

    public ExtendedPositionService(IExtendedPositionTransport transport)
        => _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public void RecordDeathPosition(
        PlayerSnapshot player,
        PlayerWorldPosition position)
    {
        ArgumentNullException.ThrowIfNull(player);
        lock (_gate)
        {
            _deathPositions[player.SessionId] = position;
        }
    }

    public bool TryGetDeathPosition(
        PlayerSessionId sessionId,
        out PlayerWorldPosition position)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        lock (_gate)
        {
            return _deathPositions.TryGetValue(sessionId, out position);
        }
    }

    public ValueTask ForgetSessionAsync(
        PlayerSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _deathPositions.Remove(sessionId);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ForgetAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _deathPositions.Clear();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RespawnAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default)
        => _transport.RespawnAsync(player, cancellationToken);

    public async ValueTask<bool> ReviveAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetDeathPosition(player.SessionId, out var position))
        {
            return false;
        }

        await _transport.RespawnAsync(player, cancellationToken).ConfigureAwait(false);
        await _transport.TeleportAsync(player, position, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public ValueTask TeleportAsync(
        PlayerSnapshot player,
        PlayerWorldPosition position,
        CancellationToken cancellationToken = default)
        => _transport.TeleportAsync(player, position, cancellationToken);

    public async ValueTask TeleportToAsync(
        PlayerSnapshot player,
        PlayerSnapshot destination,
        CancellationToken cancellationToken = default)
    {
        var position = await _transport.ReadPositionAsync(
            destination, cancellationToken).ConfigureAwait(false);
        await _transport.TeleportAsync(
            player, position, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask MoveVerticalAsync(
        PlayerSnapshot player,
        float offset,
        CancellationToken cancellationToken = default)
    {
        var current = await _transport.ReadPositionAsync(
            player, cancellationToken).ConfigureAwait(false);
        await _transport.TeleportAsync(
            player, current.OffsetZ(offset), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask SlapAsync(
        PlayerSnapshot player,
        int damage,
        CancellationToken cancellationToken = default)
        => _transport.SlapAsync(player, damage, cancellationToken);
}
