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

        return await PublishSelectedAsync(manager, distinctEligiblePlayers, selected, options, now, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<MapDefinition> GetAvailableMaps()
    {
        var catalog = _catalog() ?? throw new InvalidOperationException("AnoVeto map catalog unavailable.");
        return catalog.All.ToArray();
    }

    /// <summary>
    /// Starts a normal public vote from an explicitly confirmed eight-map pool.
    /// Maps are resolved from the live catalog, never trusted from the HUD or preset labels.
    /// </summary>
    public async ValueTask<AnoVetoOperationResult> CreateSelectedAsync(
        PlayerId manager,
        IReadOnlyCollection<PlayerId> eligiblePlayers,
        IReadOnlyCollection<string> mapIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(eligiblePlayers);
        ArgumentNullException.ThrowIfNull(mapIds);

        lock (_gate)
        {
            if (_active is not null)
                return AnoVetoOperationResult.Reject(AnoVetoFailure.AlreadyActive);
        }

        var ids = mapIds.ToArray();
        if (ids.Length != MapCount || ids.Any(string.IsNullOrWhiteSpace)
            || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != MapCount)
            return AnoVetoOperationResult.Reject(AnoVetoFailure.InvalidMap);

        var catalog = _catalog() ?? throw new InvalidOperationException("AnoVeto map catalog provider returned no catalog.");
        var lookup = catalog.All.ToDictionary(map => map.MapId, StringComparer.OrdinalIgnoreCase);
        var selected = new List<MapDefinition>(MapCount);
        foreach (var id in ids)
        {
            if (!lookup.TryGetValue(id, out var map))
                return AnoVetoOperationResult.Reject(AnoVetoFailure.InvalidMap);
            selected.Add(map);
        }

        var options = _options() ?? throw new InvalidOperationException("AnoVeto options provider returned no options.");
        var eligible = eligiblePlayers.Distinct().ToArray();
        if (eligible.Length < options.MinimumVotes)
            return AnoVetoOperationResult.Reject(AnoVetoFailure.NotEnoughEligiblePlayers);

        return await PublishSelectedAsync(manager, eligible, selected, options, now, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AnoVetoOperationResult> PublishSelectedAsync(
        PlayerId manager,
        IReadOnlyCollection<PlayerId> eligiblePlayers,
        IReadOnlyList<MapDefinition> selected,
        AnoVetoOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {

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
            eligiblePlayers,
            new VotePolicy(options.Duration, options.MinimumVotes, options.TieBreakPolicy, options.MaxVotesPerPlayer));

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

    public bool TryGetPlayerSelections(
        PlayerId playerId,
        out IReadOnlyList<MapDefinition> selections,
        out int maxVotesPerPlayer)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        lock (_gate)
        {
            if (_active is null || !_votes.TryGet(VoteId, out var snapshot)
                || snapshot is null || snapshot.State != VoteState.Open)
            {
                selections = [];
                maxVotesPerPlayer = 0;
                return false;
            }

            maxVotesPerPlayer = snapshot.Definition.Policy.MaxVotesPerPlayer;
            if (!snapshot.Selections.TryGetValue(playerId, out var chosen))
            {
                selections = [];
                return true;
            }

            var chosenIds = chosen.ToHashSet(StringComparer.Ordinal);
            selections = _active.Maps
                .Where((_, index) => chosenIds.Contains($"map{index + 1:00}"))
                .ToArray();
            return true;
        }
    }

    public bool TryGetBallotStatus(out IReadOnlyList<PlayerId> confirmed, out int eligibleCount)
    {
        lock (_gate)
        {
            confirmed = [];
            eligibleCount = 0;
            if (_active is null || !_votes.TryGet(VoteId, out var vote) || vote is null
                || vote.State != VoteState.Open) return false;
            confirmed = vote.Selections.Keys.OrderBy(player => player.SteamId64).ToArray();
            eligibleCount = vote.Definition.EligiblePlayers.Count;
            return true;
        }
    }

    public bool TryGetDraft(PlayerId player, out IReadOnlyList<MapDefinition> maps, out int maximum, out bool confirmed)
    {
        lock (_gate)
        {
            maps = [];
            maximum = 0;
            confirmed = false;
            if (_active is null || !_votes.TryGet(VoteId, out var vote) || vote is null
                || vote.State != VoteState.Open) return false;
            maximum = Math.Min(3, vote.Definition.Policy.MaxVotesPerPlayer);
            confirmed = vote.Selections.ContainsKey(player);
            if (confirmed) return TryGetPlayerSelections(player, out maps, out _);
            if (_active.Drafts.TryGetValue(player, out var draft)) maps = draft.ToArray();
            return true;
        }
    }

    public AnoVetoOperationResult ToggleSelection(PlayerId player, string mapId, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_active is null || !_votes.TryGet(VoteId, out var vote) || vote is null
                || vote.State != VoteState.Open || now >= vote.Deadline)
                return AnoVetoOperationResult.Reject(AnoVetoFailure.NotActive);
            if (!vote.Definition.EligiblePlayers.Contains(player))
                return AnoVetoOperationResult.Reject(AnoVetoFailure.NotEligible);
            if (vote.Selections.ContainsKey(player))
                return AnoVetoOperationResult.Reject(AnoVetoFailure.AlreadyVoted);
            var map = _active.Maps.FirstOrDefault(map => string.Equals(map.MapId, mapId, StringComparison.OrdinalIgnoreCase));
            if (map is null) return AnoVetoOperationResult.Reject(AnoVetoFailure.InvalidMap);
            if (!_active.Drafts.TryGetValue(player, out var draft))
                _active.Drafts[player] = draft = [];
            if (!draft.Remove(map))
            {
                if (draft.Count >= Math.Min(3, vote.Definition.Policy.MaxVotesPerPlayer))
                    return AnoVetoOperationResult.Reject(AnoVetoFailure.VoteRejected);
                draft.Add(map);
            }
            return AnoVetoOperationResult.Success(_active.Maps);
        }
    }

    public async ValueTask<AnoVetoOperationResult> ConfirmSelectionsAsync(
        PlayerId player, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ActiveVote active;
        string[] choices;
        lock (_gate)
        {
            if (_active is null) return AnoVetoOperationResult.Reject(AnoVetoFailure.NotActive);
            active = _active;
            if (!active.Drafts.TryGetValue(player, out var draft) || draft.Count == 0)
                return AnoVetoOperationResult.Reject(AnoVetoFailure.InvalidMap);
            choices = draft.Select(map => active.OptionToMap.Single(pair => pair.Value == map).Key).ToArray();
        }
        var submitted = await _votes.SubmitBallotAsync(VoteId, player, choices, now, cancellationToken).ConfigureAwait(false);
        if (!submitted.Accepted) return AnoVetoOperationResult.Reject(MapFailure(submitted.Failure));
        var result = _votes.FinalizeIfAllEligibleVoted(VoteId, now);
        return result is null ? AnoVetoOperationResult.Success(active.Maps)
            : await FinalizeCoreAsync(active, result, cancellationToken).ConfigureAwait(false);
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
        IReadOnlyDictionary<string, MapDefinition> OptionToMap)
    {
        public Dictionary<PlayerId, List<MapDefinition>> Drafts { get; } = [];
    }
}
