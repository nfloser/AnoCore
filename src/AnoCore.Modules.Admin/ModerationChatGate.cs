using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public enum ChatInterceptionDecision
{
    Allow = 1,
    Block = 2,
}

public sealed class ModerationChatGate
{
    private readonly IModerationCommunicationPolicy _policy;

    public ModerationChatGate(IModerationCommunicationPolicy policy)
        => _policy = policy ?? throw new ArgumentNullException(nameof(policy));

    public ChatInterceptionDecision Evaluate(PlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(playerId);

        return _policy.Evaluate(playerId, CommunicationChannel.Chat) switch
        {
            CommunicationRestrictionDecision.Allowed => ChatInterceptionDecision.Allow,
            CommunicationRestrictionDecision.Blocked => ChatInterceptionDecision.Block,
            CommunicationRestrictionDecision.SnapshotUnavailable => ChatInterceptionDecision.Block,
            _ => throw new InvalidOperationException("Unknown communication restriction decision."),
        };
    }
}
