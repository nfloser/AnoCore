using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ModerationChatGateTests
{
    private static readonly PlayerId Player = new(76561198000007001);

    [TestMethod]
    public void Evaluate_AllowsWhenCommunicationPolicyAllowsChat()
    {
        var gate = new ModerationChatGate(
            new StubPolicy(CommunicationRestrictionDecision.Allowed));

        Assert.AreEqual(ChatInterceptionDecision.Allow, gate.Evaluate(Player));
    }

    [TestMethod]
    public void Evaluate_BlocksWhenCommunicationPolicyBlocksChat()
    {
        var gate = new ModerationChatGate(
            new StubPolicy(CommunicationRestrictionDecision.Blocked));

        Assert.AreEqual(ChatInterceptionDecision.Block, gate.Evaluate(Player));
    }

    [TestMethod]
    public void Evaluate_BlocksFailClosedWhenSnapshotIsUnavailable()
    {
        var gate = new ModerationChatGate(
            new StubPolicy(CommunicationRestrictionDecision.SnapshotUnavailable));

        Assert.AreEqual(ChatInterceptionDecision.Block, gate.Evaluate(Player));
    }

    private sealed class StubPolicy(CommunicationRestrictionDecision decision)
        : IModerationCommunicationPolicy
    {
        public CommunicationRestrictionDecision Evaluate(
            PlayerId playerId,
            CommunicationChannel channel)
        {
            Assert.AreEqual(Player, playerId);
            Assert.AreEqual(CommunicationChannel.Chat, channel);
            return decision;
        }
    }
}
