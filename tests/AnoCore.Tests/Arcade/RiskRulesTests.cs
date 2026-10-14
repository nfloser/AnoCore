using System.Text.Json;
using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class RiskRulesTests
{
    [DataTestMethod]
    [DataRow("anocasino-risk-v1", 50, 5, 1_000_000L)]
    public void RulesExposePinnedVersionOddsAndLimits(
        string expectedVersion,
        int expectedWinOddsPercent,
        int expectedMaximumGambles,
        long expectedMaximumHeldExposure)
    {
        Assert.AreEqual(expectedVersion, RiskRules.Version);
        Assert.AreEqual(expectedWinOddsPercent, RiskRules.WinOddsPercent);
        Assert.AreEqual(expectedMaximumGambles, RiskRules.MaximumGambles);
        Assert.AreEqual(expectedMaximumHeldExposure, RiskRules.MaximumHeldExposure);
    }

    [TestMethod]
    public void BankCashoutReturnsExactInitialHeldPayoutAndTerminates()
    {
        var initial = RiskRules.Start(12_345);
        var result = RiskRules.Bank(initial, expectedRevision: 0);

        Assert.AreEqual(RiskRoundStatus.Banked, result.Status);
        Assert.AreEqual(12_345L, result.FinalPayout);
        Assert.AreEqual(12_345L, result.HeldPayout);
        Assert.AreEqual(1, result.Revision);
    }

    [TestMethod]
    public void DoubleWinDoublesHeldPayoutAndLossEndsAtZero()
    {
        var initial = RiskRules.Start(12_000);
        var won = RiskRules.Double(initial, expectedRevision: 0, won: true);
        Assert.AreEqual(24_000L, won.HeldPayout);
        Assert.AreEqual(1, won.Gambles);
        Assert.AreEqual(RiskRoundStatus.Active, won.Status);
        Assert.IsNull(won.FinalPayout);

        var lost = RiskRules.Double(initial, expectedRevision: 0, won: false);
        Assert.AreEqual(0L, lost.HeldPayout);
        Assert.AreEqual(0L, lost.FinalPayout);
        Assert.AreEqual(RiskRoundStatus.Lost, lost.Status);
    }

    [TestMethod]
    public void FifthWinMayBeBankedButCannotBeDoubledAgain()
    {
        var state = RiskRules.Start(1_000);
        for (var i = 0; i < RiskRules.MaximumGambles; i++)
            state = RiskRules.Double(state, state.Revision, won: true);

        Assert.AreEqual(RiskRules.MaximumGambles, state.Gambles);
        Assert.AreEqual(32_000L, state.HeldPayout);
        Assert.ThrowsExactly<InvalidOperationException>(() => RiskRules.Double(state, state.Revision, won: true));
        Assert.AreEqual(32_000L, RiskRules.Bank(state, state.Revision).FinalPayout);
    }

    [TestMethod]
    public void ExposureBoundsStartAndWinningDoubleBeforeOverflow()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RiskRules.Start(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RiskRules.Start(RiskRules.MaximumHeldExposure + 1));

        var state = RiskRules.Start(600_000);
        Assert.ThrowsExactly<InvalidOperationException>(() => RiskRules.Double(state, 0, won: true));
        Assert.ThrowsExactly<InvalidOperationException>(() => RiskRules.Double(state, 0, won: false));

        var max = RiskRules.Double(RiskRules.Start(500_000), 0, won: true);
        Assert.AreEqual(RiskRules.MaximumHeldExposure, max.HeldPayout);
        Assert.ThrowsExactly<InvalidOperationException>(() => RiskRules.Double(max, max.Revision, won: true));
    }

    [TestMethod]
    public void ExpectedRevisionRejectsStaleAndTerminalReplays()
    {
        var initial = RiskRules.Start(500);
        Assert.ThrowsExactly<InvalidOperationException>(() => RiskRules.Bank(initial, expectedRevision: 1));
        var banked = RiskRules.Bank(initial, expectedRevision: 0);
        Assert.ThrowsExactly<InvalidOperationException>(() => RiskRules.Bank(banked, expectedRevision: 1));
        Assert.ThrowsExactly<InvalidOperationException>(() => RiskRules.Double(banked, expectedRevision: 1, won: true));

        var lost = RiskRules.Double(initial, expectedRevision: 0, won: false);
        Assert.ThrowsExactly<InvalidOperationException>(() => RiskRules.Bank(lost, expectedRevision: 1));
    }

    [TestMethod]
    public void StateRoundTripsThroughSystemTextJsonAndRemainsImmutable()
    {
        var source = RiskRules.Double(RiskRules.Start(2_000), expectedRevision: 0, won: true);
        var json = JsonSerializer.Serialize(source);
        var restored = JsonSerializer.Deserialize<RiskRoundState>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual(source, restored);
        Assert.AreEqual("anocasino-risk-v1", restored.RulesVersion);
    }
}
