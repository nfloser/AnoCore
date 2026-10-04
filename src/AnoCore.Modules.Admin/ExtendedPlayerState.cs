using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public enum ExtendedPlayerStateOperation
{
    SetHealth = 1,
    SetArmor = 2,
    Freeze = 3,
    Unfreeze = 4,
    Noclip = 5,
    Walk = 6,
    Slay = 7,
    SetSpeed = 8,
    ResetSpeed = 9,
    Blind = 10,
    Unblind = 11,
    God = 12,
    Ungod = 13,
}

public enum ExtendedPlayerStateFacet
{
    Movement = 1,
    Speed = 2,
    Blindness = 3,
    Damage = 4,
}

public sealed record ExtendedPlayerStateMutation(
    ExtendedPlayerStateOperation Operation,
    int? Value = null);

public sealed record ExtendedPlayerStateBaseline(
    ExtendedPlayerStateFacet Facet,
    byte MoveType = 0,
    byte ActualMoveType = 0,
    float Primary = 0,
    float Secondary = 0,
    float Tertiary = 0,
    float Quaternary = 0,
    bool Flag = false);

public interface IExtendedPlayerStateTransport
{
    ValueTask<ExtendedPlayerStateBaseline> CaptureAsync(
        PlayerSnapshot player,
        ExtendedPlayerStateFacet facet,
        CancellationToken cancellationToken = default);

    ValueTask ApplyAsync(
        PlayerSnapshot player,
        ExtendedPlayerStateMutation mutation,
        CancellationToken cancellationToken = default);

    ValueTask RestoreAsync(
        PlayerSnapshot player,
        ExtendedPlayerStateBaseline baseline,
        CancellationToken cancellationToken = default);
}

public sealed class ExtendedPlayerStateService : IDisposable
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly Dictionary<PlayerSessionId, SessionOwnership> _owned = [];
    private readonly IExtendedPlayerStateTransport _transport;
    private int _disposed;

    public ExtendedPlayerStateService(IExtendedPlayerStateTransport transport)
        => _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public async ValueTask<bool> ApplyAsync(
        PlayerSnapshot player,
        ExtendedPlayerStateMutation mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(mutation);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (TryGetReleaseFacet(mutation.Operation, out var releaseFacet))
            {
                return await ReleaseFacetUnsafeAsync(
                    player, releaseFacet, cancellationToken).ConfigureAwait(false);
            }

            if (!TryGetOwnedFacet(mutation.Operation, out var facet))
            {
                await _transport.ApplyAsync(player, mutation, cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }

            if (!_owned.TryGetValue(player.SessionId, out var session))
            {
                session = new SessionOwnership(player);
                _owned.Add(player.SessionId, session);
            }

            if (!session.Baselines.ContainsKey(facet))
            {
                var baseline = await _transport.CaptureAsync(
                    player, facet, cancellationToken).ConfigureAwait(false);

                try
                {
                    await _transport.ApplyAsync(player, mutation, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    if (session.Baselines.Count == 0)
                    {
                        _owned.Remove(player.SessionId);
                    }

                    throw;
                }

                session.Baselines.Add(facet, baseline);
                return true;
            }

            await _transport.ApplyAsync(player, mutation, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        finally
        {
            _operations.Release();
        }
    }

    public async ValueTask ReleaseSessionAsync(
        PlayerSnapshot player,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_owned.Remove(player.SessionId, out var session))
            {
                return;
            }

            foreach (var baseline in session.Baselines.Values.Reverse())
            {
                await _transport.RestoreAsync(
                    session.Player, baseline, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _operations.Release();
        }
    }

    public async ValueTask ReleaseAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sessions = _owned.Values.ToArray();
            _owned.Clear();

            foreach (var session in sessions)
            {
                foreach (var baseline in session.Baselines.Values.Reverse())
                {
                    await _transport.RestoreAsync(
                        session.Player, baseline, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _operations.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _owned.Clear();
        _operations.Dispose();
    }

    private async ValueTask<bool> ReleaseFacetUnsafeAsync(
        PlayerSnapshot player,
        ExtendedPlayerStateFacet facet,
        CancellationToken cancellationToken)
    {
        if (!_owned.TryGetValue(player.SessionId, out var session)
            || !session.Baselines.Remove(facet, out var baseline))
        {
            return false;
        }

        try
        {
            await _transport.RestoreAsync(
                session.Player, baseline, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            session.Baselines.Add(facet, baseline);
            throw;
        }

        if (session.Baselines.Count == 0)
        {
            _owned.Remove(player.SessionId);
        }

        return true;
    }

    private static bool TryGetOwnedFacet(
        ExtendedPlayerStateOperation operation,
        out ExtendedPlayerStateFacet facet)
    {
        switch (operation)
        {
            case ExtendedPlayerStateOperation.Freeze:
            case ExtendedPlayerStateOperation.Noclip:
                facet = ExtendedPlayerStateFacet.Movement;
                return true;
            case ExtendedPlayerStateOperation.SetSpeed:
                facet = ExtendedPlayerStateFacet.Speed;
                return true;
            case ExtendedPlayerStateOperation.Blind:
                facet = ExtendedPlayerStateFacet.Blindness;
                return true;
            case ExtendedPlayerStateOperation.God:
                facet = ExtendedPlayerStateFacet.Damage;
                return true;
            default:
                facet = default;
                return false;
        }
    }

    private static bool TryGetReleaseFacet(
        ExtendedPlayerStateOperation operation,
        out ExtendedPlayerStateFacet facet)
    {
        switch (operation)
        {
            case ExtendedPlayerStateOperation.Unfreeze:
            case ExtendedPlayerStateOperation.Walk:
                facet = ExtendedPlayerStateFacet.Movement;
                return true;
            case ExtendedPlayerStateOperation.ResetSpeed:
                facet = ExtendedPlayerStateFacet.Speed;
                return true;
            case ExtendedPlayerStateOperation.Unblind:
                facet = ExtendedPlayerStateFacet.Blindness;
                return true;
            case ExtendedPlayerStateOperation.Ungod:
                facet = ExtendedPlayerStateFacet.Damage;
                return true;
            default:
                facet = default;
                return false;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private sealed class SessionOwnership(PlayerSnapshot player)
    {
        public PlayerSnapshot Player { get; } = player;
        public Dictionary<ExtendedPlayerStateFacet, ExtendedPlayerStateBaseline> Baselines { get; } = [];
    }
}
