using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public enum CommunicationChannel
{
    Chat = 1,
    Voice = 2,
}

public enum CommunicationRestrictionDecision
{
    Allowed = 1,
    Blocked = 2,
    SnapshotUnavailable = 3,
}

public interface IModerationCommunicationPolicy
{
    CommunicationRestrictionDecision Evaluate(
        PlayerId playerId,
        CommunicationChannel channel);
}

public sealed class ModerationCommunicationPolicy : IModerationCommunicationPolicy
{
    private readonly IModerationSnapshotProvider _snapshots;
    private readonly TimeProvider _timeProvider;

    public ModerationCommunicationPolicy(
        IModerationSnapshotProvider snapshots,
        TimeProvider? timeProvider = null)
    {
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CommunicationRestrictionDecision Evaluate(
        PlayerId playerId,
        CommunicationChannel channel)
    {
        ArgumentNullException.ThrowIfNull(playerId);

        var restriction = channel switch
        {
            CommunicationChannel.Chat => ModerationRestriction.Chat,
            CommunicationChannel.Voice => ModerationRestriction.Voice,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null),
        };

        if (!_snapshots.TryGetRestrictions(
                playerId,
                _timeProvider.GetUtcNow().ToUniversalTime(),
                out var restrictions))
        {
            return CommunicationRestrictionDecision.SnapshotUnavailable;
        }

        return restrictions.HasFlag(restriction)
            ? CommunicationRestrictionDecision.Blocked
            : CommunicationRestrictionDecision.Allowed;
    }
}
