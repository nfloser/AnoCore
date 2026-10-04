using AnoCore.Abstractions.Maps;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;

namespace AnoCore.Modules.AnoVeto;

public sealed class AnoVetoCoordinator
{
    public static readonly VoteId VoteId = new("ano.anoveto");
    private const int MapCount = 8;

    private readonly object _gate = new();
    private readonly Func<IMapCatalog> _catalog;
    private readonly IVoteService _votes;
    private readonly IMapChanger _mapChanger;
    private readonly IAnoVetoRandomSource _random;
    private readonly Func<AnoVetoOptions> _options;
    private ActiveVote? _active;
    private AnoVetoOperationResult? _lastFinalized;

    public AnoVetoCoordinator(
        IMapCatalog catalog,
        IVoteService votes,
        IMapChanger mapChanger,
        IAnoVetoRandomSource random,
        AnoVetoOptions options)
        : this(
            () => catalog,
            votes,
            mapChanger,
            random,
            () => options)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
    }

    internal AnoVetoCoordinator(
        Func<IMapCatalog> catalog,
        IVoteService votes,
        IMapChanger mapChanger,
        IAnoVetoRandomSource random,
        Func<AnoVetoOptions> options)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _votes = votes ?? throw new ArgumentNullException(nameof(votes));
        _mapChanger = mapChanger ?? throw new ArgumentNullException(nameof(mapChanger));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<AnoVetoOperationResult> CreateAsync(
        PlayerId manager,
        IReadOnlyCollection<PlayerId> eligiblePlayers,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(eligiblePlayers);

        lock (_gate)
        {
            if (_active is not null)
            {
                return AnoVetoOperationResult.Reject(AnoVetoFailure.AlreadyActive);
            }
        }

        var catalog = _catalog()
            ?? throw new InvalidOperationException("AnoVeto map catalog provider returned no catalog.");
        var options = _options()
            ?? throw new InvalidOperationException("AnoVeto options provider returned no options.");

        if (catalog.All.Count < MapCount)
        {
            return AnoVetoOperationResult.Reject(AnoVetoFailure.NotEnoughMaps);
        }

        var distinctEligiblePlayers = eligiblePlayers.Distinct().ToArray();
        if (distinctEligiblePlayers.Length < options.MinimumVotes)
        {
            return AnoVetoOperationResult.Reject(AnoVetoFailure.NotEnoughEligiblePlayers);
        }

        var selected = _random.Select(catalog.All, MapCount).ToArray();
        if (selected.Length != MapCount || selected.Select(map => map.MapId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != MapCount)
        {
            throw new InvalidOperationException("AnoVeto random selection must return exactly eight unique maps.");
        }

        var voteOptions = selected
            .Select((map, index) => new VoteOption($"map{index + 1:00}", map.DisplayName))
            .ToArray();
        var optionToMap = voteOptions
            .Select((option, index) => new KeyValuePair<string, MapDefinition>(option.Id, selected[index]))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var definition = new VoteDefinition(
            VoteId,
            "AnoVeto — choose the next map",
            voteOptions,
            distinctEligiblePlayers,
            new VotePolicy(options.Duration, options.MinimumVotes, options.TieBreakPolicy));

        var created = await _votes.CreateAsync(manager, definition, now, cancellationToken).ConfigureAwait(false);
        if (!created.Accepted)
        {
            return AnoVetoOperationResult.Reject(MapFailure(created.Failure));
        }

        lock (_gate)
        {
            _active = new ActiveVote(selected, optionToMap);
        }

        return AnoVetoOperationResult.Success(selected);
    }

    public ValueTask<AnoVetoOperationResult> CastAsync(
        PlayerId player,
        string mapId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        var normalized = mapId?.Trim() ?? string.Empty;
        ActiveVote? active;
        lock (_gate)
        {
            active = _active;
        }

        if (active is null)
        {
            return ValueTask.FromResult(AnoVetoOperationResult.Reject(AnoVetoFailure.NotActive));
        }

        var option = active.OptionToMap.FirstOrDefault(pair => string.Equals(pair.Value.MapId, normalized, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(option.Key))
        {
            return ValueTask.FromResult(AnoVetoOperationResult.Reject(AnoVetoFailure.InvalidMap));
        }

        return CastCoreAsync(player, option.Key, active, now, cancellationToken);
    }

    public async ValueTask<AnoVetoOperationResult> CompleteAsync(
        PlayerId manager,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ActiveVote? active;
        lock (_gate)
        {
            active = _active;
        }

        if (active is null)
        {
            return AnoVetoOperationResult.Reject(AnoVetoFailure.NotActive);
        }

        var closed = await _votes.CloseAsync(manager, VoteId, now, cancellationToken).ConfigureAwait(false);
        if (!closed.Accepted || closed.Result is null)
        {
            return AnoVetoOperationResult.Reject(MapFailure(closed.Failure));
        }

        return await FinalizeCoreAsync(active, closed.Result, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AnoVetoOperationResult?> ExpireAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ActiveVote? active;
        lock (_gate)
        {
            active = _active;
        }

        if (active is null)
        {
            return null;
        }

        var finalized = _votes.FinalizeExpired(now).FirstOrDefault(result => result.VoteId == VoteId);
        if (finalized is null)
        {
            return null;
        }

        return await FinalizeCoreAsync(active, finalized, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AnoVetoOperationResult> CancelAsync(
        PlayerId manager,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ActiveVote? active;
        lock (_gate)
        {
            active = _active;
        }

        if (active is null)
        {
            return AnoVetoOperationResult.Reject(AnoVetoFailure.NotActive);
        }

        var cancelled = await _votes.CancelAsync(manager, VoteId, now, cancellationToken).ConfigureAwait(false);
        if (!cancelled.Accepted)
        {
            return AnoVetoOperationResult.Reject(MapFailure(cancelled.Failure));
        }

        lock (_gate)
        {
            if (ReferenceEquals(_active, active))
            {
                _active = null;
            }
        }

        return AnoVetoOperationResult.Success(active.Maps, AnoVetoOutcome.Cancelled);
    }

    public bool TryTakeFinalized(out AnoVetoOperationResult? result)
    {
        lock (_gate)
        {
            result = _lastFinalized;
            _lastFinalized = null;
            return result is not null;
        }
    }

    public bool TryGetStatus(out IReadOnlyList<MapDefinition> maps)
    {
        lock (_gate)
        {
            if (_active is null)
            {
                maps = [];
                return false;
            }

            maps = _active.Maps;
            return true;
        }
    }

    private async ValueTask<AnoVetoOperationResult> CastCoreAsync(
        PlayerId player,
        string optionId,
        ActiveVote active,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var cast = await _votes.CastAsync(VoteId, player, optionId, now, cancellationToken).ConfigureAwait(false);
        if (!cast.Accepted)
        {
            return AnoVetoOperationResult.Reject(MapFailure(cast.Failure));
        }

        var finalized = _votes.FinalizeIfAllEligibleVoted(VoteId, now);
        if (finalized is null)
        {
            return AnoVetoOperationResult.Success(active.Maps);
        }

        return await FinalizeCoreAsync(active, finalized, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AnoVetoOperationResult> FinalizeCoreAsync(
        ActiveVote active,
        VoteResult result,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, active))
            {
                return AnoVetoOperationResult.Reject(AnoVetoFailure.NotActive);
            }

            _active = null;
        }

        if (result.Outcome == VoteOutcome.QuorumNotMet)
        {
            return RememberFinalized(
                AnoVetoOperationResult.Success(active.Maps, AnoVetoOutcome.QuorumNotMet));
        }

        if (result.Outcome == VoteOutcome.TieWithoutWinner || result.WinningOptionId is null)
        {
            return RememberFinalized(
                AnoVetoOperationResult.Success(active.Maps, AnoVetoOutcome.TieWithoutWinner));
        }

        if (!active.OptionToMap.TryGetValue(result.WinningOptionId, out var winner))
        {
            throw new InvalidOperationException("Vote result references an unknown AnoVeto option.");
        }

        await _mapChanger.ChangeMapAsync(winner, cancellationToken).ConfigureAwait(false);
        return RememberFinalized(
            AnoVetoOperationResult.Success(active.Maps, AnoVetoOutcome.MapSelected, winner));
    }

    private AnoVetoOperationResult RememberFinalized(AnoVetoOperationResult result)
    {
        lock (_gate)
        {
            _lastFinalized = result;
        }
        return result;
    }

    private static AnoVetoFailure MapFailure(VoteOperationFailure failure)
        => failure switch
        {
            VoteOperationFailure.Forbidden => AnoVetoFailure.Forbidden,
            VoteOperationFailure.AlreadyExists => AnoVetoFailure.AlreadyActive,
            VoteOperationFailure.NotFound or VoteOperationFailure.NotOpen => AnoVetoFailure.NotActive,
            VoteOperationFailure.NotEligible => AnoVetoFailure.NotEligible,
            VoteOperationFailure.AlreadyVoted => AnoVetoFailure.AlreadyVoted,
            VoteOperationFailure.InvalidOption => AnoVetoFailure.InvalidMap,
            _ => AnoVetoFailure.VoteRejected,
        };

    private sealed record ActiveVote(
        IReadOnlyList<MapDefinition> Maps,
        IReadOnlyDictionary<string, MapDefinition> OptionToMap);
}
