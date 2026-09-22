using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ModerationVoiceGateTests
{
    private static readonly PlayerId Player = new(76561198000008001);

    [TestMethod]
    public void Evaluate_AllowsWhenCommunicationPolicyAllowsVoice()
    {
        var gate = new ModerationVoiceGate(
            new StubPolicy(CommunicationRestrictionDecision.Allowed));

        Assert.AreEqual(VoiceInterceptionDecision.Allow, gate.Evaluate(Player));
    }

    [TestMethod]
    public void Evaluate_BlocksWhenCommunicationPolicyBlocksVoice()
    {
        var gate = new ModerationVoiceGate(
            new StubPolicy(CommunicationRestrictionDecision.Blocked));

        Assert.AreEqual(VoiceInterceptionDecision.Block, gate.Evaluate(Player));
    }

    [TestMethod]
    public void Evaluate_BlocksFailClosedWhenSnapshotIsUnavailable()
    {
        var gate = new ModerationVoiceGate(
            new StubPolicy(CommunicationRestrictionDecision.SnapshotUnavailable));

        Assert.AreEqual(VoiceInterceptionDecision.Block, gate.Evaluate(Player));
    }

    private sealed class StubPolicy(CommunicationRestrictionDecision decision)
        : IModerationCommunicationPolicy
    {
        public CommunicationRestrictionDecision Evaluate(
            PlayerId playerId,
            CommunicationChannel channel)
        {
            Assert.AreEqual(Player, playerId);
            Assert.AreEqual(CommunicationChannel.Voice, channel);
            return decision;
        }
    }
}
