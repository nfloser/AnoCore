using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Tournament;

public interface ITournamentTeamAssignmentSource
{
    Guid? ActiveMatchId { get; }

    bool TryGetAssignedSide(PlayerId playerId, out PlayerTeam side);
}

public sealed class TournamentMatchRuntime : ITournamentTeamAssignmentSource
{
    private readonly object _gate = new();
    private TournamentRecoverySession? _active;

    private TournamentMatchRuntime(TournamentRecoverySession? active)
        => _active = active;

    public Guid? ActiveMatchId
    {
        get
        {
            lock (_gate)
            {
                return _active?.Machine.Configuration.MatchId;
            }
        }
    }

    public static async Task<TournamentMatchRuntime> CreateAsync(
        TournamentRecoveryService recovery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        var active = await recovery.RestoreActiveAsync(cancellationToken).ConfigureAwait(false);
        return new TournamentMatchRuntime(active);
    }

    public bool TryGetAssignedSide(PlayerId playerId, out PlayerTeam side)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        lock (_gate)
        {
            var assigned = _active?.Machine.AssignedSide(playerId);
            if (assigned is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist)
            {
                side = assigned.Value;
                return true;
            }

            side = PlayerTeam.Unknown;
            return false;
        }
    }

    public TournamentRecoverySession? CurrentSession
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    public void Replace(TournamentRecoverySession? session)
    {
        lock (_gate)
        {
            _active = session;
        }
    }
}
