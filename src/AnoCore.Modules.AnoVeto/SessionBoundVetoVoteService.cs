using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
using AnoCore.Runtime.Voting;

namespace AnoCore.Modules.AnoVeto;

// A separate vote store confines native CSS authority to AnoVeto. Other consumers
// keep the shared service's existing ano.vote.manage policy.
public sealed class SessionBoundVetoVoteService : IVoteService
{
    public const string RequiredFlag = "@anocore/veto";
    private readonly IPlayerRegistry _players;
    private readonly Action<Action> _schedule;
    private readonly Func<PlayerSnapshot, bool> _hasFlag;
    private readonly Func<bool> _isActive;
    private readonly VoteService _votes = new(new ScopedPermissions());

    public SessionBoundVetoVoteService(IPlayerRegistry players, Action<Action> schedule,
        Func<PlayerSnapshot, bool> hasFlag, Func<bool> isActive)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _hasFlag = hasFlag ?? throw new ArgumentNullException(nameof(hasFlag));
        _isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
    }

    public ValueTask<VoteOperationResult> CreateAsync(PlayerId caller, VoteDefinition definition,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return ManageAsync(caller, definition.Id, () => _votes.CreateAsync(caller, definition, now, cancellationToken), cancellationToken);
    }

    public ValueTask<VoteOperationResult> CloseAsync(PlayerId caller, VoteId voteId,
        DateTimeOffset now, CancellationToken cancellationToken = default)
        => ManageAsync(caller, voteId, () => _votes.CloseAsync(caller, voteId, now, cancellationToken), cancellationToken);

    public ValueTask<VoteOperationResult> CancelAsync(PlayerId caller, VoteId voteId,
        DateTimeOffset now, CancellationToken cancellationToken = default)
        => ManageAsync(caller, voteId, () => _votes.CancelAsync(caller, voteId, now, cancellationToken), cancellationToken);

    public ValueTask<VoteOperationResult> CastAsync(VoteId voteId, PlayerId playerId, string optionId,
        DateTimeOffset now, CancellationToken cancellationToken = default)
        => _isActive() && voteId == AnoVetoCoordinator.VoteId
            ? _votes.CastAsync(voteId, playerId, optionId, now, cancellationToken)
            : ValueTask.FromResult(Denied());

    public VoteResult? FinalizeIfAllEligibleVoted(VoteId voteId, DateTimeOffset now)
        => _isActive() && voteId == AnoVetoCoordinator.VoteId ? _votes.FinalizeIfAllEligibleVoted(voteId, now) : null;

    public IReadOnlyList<VoteResult> FinalizeExpired(DateTimeOffset now)
        => _isActive() ? _votes.FinalizeExpired(now) : [];

    public bool TryGet(VoteId voteId, out VoteSnapshot? vote)
    {
        vote = null;
        return _isActive() && voteId == AnoVetoCoordinator.VoteId && _votes.TryGet(voteId, out vote);
    }

    private ValueTask<VoteOperationResult> ManageAsync(PlayerId caller, VoteId voteId,
        Func<ValueTask<VoteOperationResult>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();
        if (voteId != AnoVetoCoordinator.VoteId || !_isActive()
            || !_players.TryGet(caller, out var expected) || expected?.IsConnected != true)
        {
            return ValueTask.FromResult(Denied());
        }

        var completion = new TaskCompletionSource<VoteOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _schedule(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_isActive() || !_players.TryGet(caller, out var current)
                        || current?.IsConnected != true || current.SessionId != expected.SessionId || !_hasFlag(current))
                    {
                        completion.TrySetResult(Denied());
                        return;
                    }

                    // The private in-memory service uses only a completed permission
                    // result: authority check and mutation finish in this same callback.
                    completion.TrySetResult(operation().GetAwaiter().GetResult());
                }
                catch (OperationCanceledException)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch
                {
                    completion.TrySetResult(Denied());
                }
            });
        }
        catch
        {
            completion.TrySetResult(Denied());
        }

        return new ValueTask<VoteOperationResult>(completion.Task.WaitAsync(cancellationToken));
    }

    private static VoteOperationResult Denied() => VoteOperationResult.Reject(VoteOperationFailure.Forbidden);

    private sealed class ScopedPermissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(permission == VoteService.ManagePermission);
    }
}
