using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Admin;

public enum VoiceInterceptionDecision
{
    Allow = 1,
    Block = 2,
}

public sealed class ModerationVoiceGate
{
    private readonly IModerationCommunicationPolicy _policy;

    public ModerationVoiceGate(IModerationCommunicationPolicy policy)
        => _policy = policy ?? throw new ArgumentNullException(nameof(policy));

    public VoiceInterceptionDecision Evaluate(PlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(playerId);

        return _policy.Evaluate(playerId, CommunicationChannel.Voice) switch
        {
            CommunicationRestrictionDecision.Allowed => VoiceInterceptionDecision.Allow,
            CommunicationRestrictionDecision.Blocked => VoiceInterceptionDecision.Block,
            CommunicationRestrictionDecision.SnapshotUnavailable => VoiceInterceptionDecision.Block,
            _ => throw new InvalidOperationException("Unknown communication restriction decision."),
        };
    }
}
