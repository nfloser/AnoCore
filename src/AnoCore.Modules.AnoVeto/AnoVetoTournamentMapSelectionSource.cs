using AnoCore.Abstractions.Players;
using AnoCore.Modules.Tournament;

namespace AnoCore.Modules.AnoVeto;

public sealed class AnoVetoTournamentMapSelectionSource : ITournamentMapSelectionSource
{
    private readonly AnoVetoCoordinator _coordinator;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private TournamentMapSelectionRequest? _active;

    public AnoVetoTournamentMapSelectionSource(
        AnoVetoCoordinator coordinator,
        TimeProvider? timeProvider = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _time = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask StartAsync(
        TournamentMapSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_active is not null)
                throw new InvalidOperationException("A tournament AnoVeto selection is already active.");
        }

        var created = await _coordinator.CreateAsync(
            request.Manager,
            request.EligiblePlayers,
            _time.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        if (!created.Accepted)
            throw new InvalidOperationException(
                $"AnoVeto could not start tournament selection: {created.Failure}.");

        lock (_gate)
        {
            if (_active is not null)
                throw new InvalidOperationException("A tournament AnoVeto selection became active concurrently.");
            _active = request;
        }
    }

    public async ValueTask<TournamentMapSelectionResult?> CompleteAsync(
        PlayerId manager,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        TournamentMapSelectionRequest active;
        lock (_gate)
        {
            active = _active
                ?? throw new InvalidOperationException("No tournament AnoVeto selection is active.");
        }

        var completed = await _coordinator.CompleteAsync(
            manager, _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (!completed.Accepted)
            throw new InvalidOperationException(
                $"AnoVeto could not complete tournament selection: {completed.Failure}.");
        if (completed.Outcome != AnoVetoOutcome.MapSelected || completed.Winner is null)
            return null;

        var wanted = (int)active.BestOf;
        var maps = new List<string>(wanted) { completed.Winner.MapId };
        maps.AddRange(completed.Maps
            .Select(map => map.MapId)
            .Where(mapId => !string.Equals(
                mapId, completed.Winner.MapId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(wanted - 1));

        if (maps.Count != wanted)
            throw new InvalidOperationException(
                $"AnoVeto result cannot resolve a BO{wanted} tournament series.");

        lock (_gate)
        {
            if (!ReferenceEquals(_active, active))
                throw new InvalidOperationException(
                    "Tournament AnoVeto selection changed while completion was running.");
            _active = null;
        }

        return new TournamentMapSelectionResult(
            active.MatchId,
            active.Revision,
            maps);
    }

    public async ValueTask CancelAsync(
        PlayerId manager,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        TournamentMapSelectionRequest active;
        lock (_gate)
        {
            active = _active
                ?? throw new InvalidOperationException("No tournament AnoVeto selection is active.");
        }

        var cancelled = await _coordinator.CancelAsync(
            manager, _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (!cancelled.Accepted)
            throw new InvalidOperationException(
                $"AnoVeto could not cancel tournament selection: {cancelled.Failure}.");

        lock (_gate)
        {
            if (ReferenceEquals(_active, active))
                _active = null;
        }
    }
}
