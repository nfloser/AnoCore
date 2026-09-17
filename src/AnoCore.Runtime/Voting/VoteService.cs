using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;

namespace AnoCore.Runtime.Voting;

public sealed class VoteService : IVoteService
{
    public static readonly PermissionId ManagePermission = new("ano.vote.manage");

    private readonly object _gate = new();
    private readonly IPermissionEvaluator _permissions;
    private readonly Dictionary<VoteId, Session> _sessions = [];

    public VoteService(IPermissionEvaluator permissions)
        => _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));

    public async ValueTask<VoteOperationResult> CreateAsync(
        PlayerId caller,
        VoteDefinition definition,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(definition);
        if (!await CanManageAsync(caller, cancellationToken).ConfigureAwait(false))
        {
            return VoteOperationResult.Reject(VoteOperationFailure.Forbidden);
        }

        lock (_gate)
        {
            if (_sessions.TryGetValue(definition.Id, out var existing) && existing.State == VoteState.Open)
            {
                return VoteOperationResult.Reject(VoteOperationFailure.AlreadyExists);
            }

            _sessions[definition.Id] = new Session(definition, now);
            return VoteOperationResult.Success();
        }
    }

    public ValueTask<VoteOperationResult> CastAsync(
        VoteId voteId,
        PlayerId playerId,
        string optionId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(voteId);
        ArgumentNullException.ThrowIfNull(playerId);

        lock (_gate)
        {
            if (!_sessions.TryGetValue(voteId, out var session))
            {
                return ValueTask.FromResult(VoteOperationResult.Reject(VoteOperationFailure.NotFound));
            }

            if (session.State != VoteState.Open || now >= session.Deadline)
            {
                return ValueTask.FromResult(VoteOperationResult.Reject(VoteOperationFailure.NotOpen));
            }

            if (!session.Eligible.Contains(playerId))
            {
                return ValueTask.FromResult(VoteOperationResult.Reject(VoteOperationFailure.NotEligible));
            }

            if (session.Ballots.ContainsKey(playerId))
            {
                return ValueTask.FromResult(VoteOperationResult.Reject(VoteOperationFailure.AlreadyVoted));
            }

            var normalizedOption = optionId?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!session.OptionIds.Contains(normalizedOption))
            {
                return ValueTask.FromResult(VoteOperationResult.Reject(VoteOperationFailure.InvalidOption));
            }

            session.Ballots.Add(playerId, normalizedOption);
            return ValueTask.FromResult(VoteOperationResult.Success());
        }
    }

    public async ValueTask<VoteOperationResult> CloseAsync(
        PlayerId caller,
        VoteId voteId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!await CanManageAsync(caller, cancellationToken).ConfigureAwait(false))
        {
            return VoteOperationResult.Reject(VoteOperationFailure.Forbidden);
        }

        lock (_gate)
        {
            if (!_sessions.TryGetValue(voteId, out var session))
            {
                return VoteOperationResult.Reject(VoteOperationFailure.NotFound);
            }

            if (session.State != VoteState.Open)
            {
                return VoteOperationResult.Reject(VoteOperationFailure.NotOpen);
            }

            return VoteOperationResult.Success(session.Finalize(now));
        }
    }

    public async ValueTask<VoteOperationResult> CancelAsync(
        PlayerId caller,
        VoteId voteId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!await CanManageAsync(caller, cancellationToken).ConfigureAwait(false))
        {
            return VoteOperationResult.Reject(VoteOperationFailure.Forbidden);
        }

        lock (_gate)
        {
            if (!_sessions.TryGetValue(voteId, out var session))
            {
                return VoteOperationResult.Reject(VoteOperationFailure.NotFound);
            }

            if (session.State != VoteState.Open)
            {
                return VoteOperationResult.Reject(VoteOperationFailure.NotOpen);
            }

            return VoteOperationResult.Success(session.Cancel(now));
        }
    }

    public IReadOnlyList<VoteResult> FinalizeExpired(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _sessions.Values
                .Where(session => session.State == VoteState.Open && now >= session.Deadline)
                .OrderBy(session => session.Definition.Id.Value, StringComparer.Ordinal)
                .Select(session => session.Finalize(now))
                .ToArray();
        }
    }

    public bool TryGet(VoteId voteId, out VoteSnapshot? vote)
    {
        ArgumentNullException.ThrowIfNull(voteId);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(voteId, out var session))
            {
                vote = null;
                return false;
            }

            vote = session.Snapshot();
            return true;
        }
    }

    private ValueTask<bool> CanManageAsync(PlayerId caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return _permissions.HasPermissionAsync(caller, ManagePermission, cancellationToken);
    }

    private sealed class Session
    {
        public Session(VoteDefinition definition, DateTimeOffset openedAt)
        {
            Definition = definition;
            OpenedAt = openedAt;
            Deadline = openedAt + definition.Policy.Duration;
            Eligible = definition.EligiblePlayers.ToHashSet();
            OptionIds = definition.Options.Select(option => option.Id).ToHashSet(StringComparer.Ordinal);
        }

        public VoteDefinition Definition { get; }
        public DateTimeOffset OpenedAt { get; }
        public DateTimeOffset Deadline { get; }
        public HashSet<PlayerId> Eligible { get; }
        public HashSet<string> OptionIds { get; }
        public Dictionary<PlayerId, string> Ballots { get; } = [];
        public VoteState State { get; private set; } = VoteState.Open;
        public VoteResult? Result { get; private set; }

        public VoteSnapshot Snapshot()
            => new(Definition, State, OpenedAt, Deadline, new Dictionary<PlayerId, string>(Ballots));

        public VoteResult Finalize(DateTimeOffset now)
        {
            if (Result is not null)
            {
                return Result;
            }

            var tallies = Definition.Options.ToDictionary(option => option.Id, _ => 0, StringComparer.Ordinal);
            foreach (var optionId in Ballots.Values)
            {
                tallies[optionId]++;
            }

            VoteOutcome outcome;
            string? winner = null;
            if (Ballots.Count < Definition.Policy.MinimumVotes)
            {
                outcome = VoteOutcome.QuorumNotMet;
            }
            else
            {
                var maximum = tallies.Values.Max();
                var tied = Definition.Options.Where(option => tallies[option.Id] == maximum).ToArray();
                if (tied.Length == 1)
                {
                    outcome = VoteOutcome.Completed;
                    winner = tied[0].Id;
                }
                else if (Definition.Policy.TieBreakPolicy == VoteTieBreakPolicy.OptionOrder)
                {
                    outcome = VoteOutcome.Completed;
                    winner = tied[0].Id;
                }
                else
                {
                    outcome = VoteOutcome.TieWithoutWinner;
                }
            }

            State = VoteState.Completed;
            Result = new VoteResult(Definition.Id, outcome, winner, tallies, Ballots.Count, now);
            return Result;
        }

        public VoteResult Cancel(DateTimeOffset now)
        {
            var tallies = Definition.Options.ToDictionary(option => option.Id, _ => 0, StringComparer.Ordinal);
            foreach (var optionId in Ballots.Values)
            {
                tallies[optionId]++;
            }

            State = VoteState.Cancelled;
            Result = new VoteResult(Definition.Id, VoteOutcome.Cancelled, null, tallies, Ballots.Count, now);
            return Result;
        }
    }
}
