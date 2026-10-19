using System.Text.Json;
using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class SpitzeRulesTests
{
    [TestMethod]
    public void FiveSkinSymbolsAndBlankStopsUsePinnedWeights()
    {
        Assert.AreEqual(SpitzeSymbol.Loss, SpitzeRules.FromRoll(0));
        Assert.AreEqual(SpitzeSymbol.SafariMesh, SpitzeRules.FromRoll(25));
        Assert.AreEqual(SpitzeSymbol.NeonRevolution, SpitzeRules.FromRoll(53));
        Assert.AreEqual(SpitzeSymbol.Gold, SpitzeRules.FromRoll(75));
        Assert.AreEqual(SpitzeSymbol.Knife, SpitzeRules.FromRoll(81));
        Assert.AreEqual(SpitzeSymbol.Blank, SpitzeRules.FromRoll(85));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SpitzeRules.FromRoll(100));
    }

    [TestMethod]
    public void EachSymbolAdvancesOnlyItsLadderAndKnifeAdvancesAllThree()
    {
        var first = SpitzeRules.Start(100, SpitzeSymbol.SafariMesh);
        Assert.AreEqual(1, first.Low);
        Assert.AreEqual(0, first.Middle);
        Assert.AreEqual(0, first.High);
        Assert.AreEqual(20L, SpitzeRules.Cashpot(first));
        var next = SpitzeRules.Spin(first, first.Revision, SpitzeSymbol.NeonRevolution);
        next = SpitzeRules.Spin(next, next.Revision, SpitzeSymbol.Gold);
        next = SpitzeRules.Spin(next, next.Revision, SpitzeSymbol.Knife);
        Assert.AreEqual(2, next.Low);
        Assert.AreEqual(2, next.Middle);
        Assert.AreEqual(2, next.High);
        Assert.AreEqual(760L, SpitzeRules.Cashpot(next));
        Assert.AreEqual(4, next.PaidSpins);
    }

    [TestMethod]
    public void LossResetsCashpotAndLaddersButNotAlreadyGrantedDirectWins()
    {
        var round = SpitzeRules.Start(100, SpitzeSymbol.Gold);
        for (var i = 0; i < 4; i++)
            round = SpitzeRules.Spin(round, round.Revision, SpitzeSymbol.Gold);
        Assert.AreEqual(5, round.High);
        Assert.AreEqual(30_000L, round.DirectWin);
        round = SpitzeRules.Spin(round, round.Revision, SpitzeSymbol.Loss);
        Assert.AreEqual(0, SpitzeRules.Cashpot(round));
        Assert.AreEqual(0, round.High);
        Assert.AreEqual(30_000L, SpitzeRules.Collect(round, round.Revision).FinalPayout);
    }

    [TestMethod]
    public void AllFullLaddersTriggerExactlySevenFreeActionGames()
    {
        var round = SpitzeRules.Start(100, SpitzeSymbol.Knife);
        for (var i = 0; i < 4; i++)
            round = SpitzeRules.Spin(round, round.Revision, SpitzeSymbol.Knife);
        Assert.AreEqual(5, round.Low);
        Assert.AreEqual(5, round.Middle);
        Assert.AreEqual(5, round.High);
        Assert.AreEqual(7, round.ActionGamesRemaining);
        Assert.IsTrue(round.ActionGamesTriggered);
        Assert.AreEqual(33_600L, round.DirectWin);
        Assert.ThrowsExactly<InvalidOperationException>(() => SpitzeRules.Spin(round, round.Revision, SpitzeSymbol.Gold));
        var debitBefore = round.PaidSpins;
        for (var i = 0; i < 7; i++)
            round = SpitzeRules.FreeSpin(round, round.Revision, SpitzeSymbol.Blank);
        Assert.AreEqual(debitBefore, round.PaidSpins);
        Assert.AreEqual(0, round.ActionGamesRemaining);
        Assert.AreEqual(SpitzeRoundStatus.Completed, round.Status);
        Assert.AreEqual(67_200L, round.FinalPayout);
    }

    [TestMethod]
    public void CollectTerminatesAtExactlyTheSumOfCashpotAndDirectSuccesses()
    {
        var round = SpitzeRules.Start(100, SpitzeSymbol.SafariMesh);
        var total = SpitzeRules.Cashpot(round) + round.DirectWin;
        var collected = SpitzeRules.Collect(round, round.Revision);
        Assert.AreEqual(total, collected.FinalPayout);
        Assert.AreEqual(SpitzeRoundStatus.Collected, collected.Status);
        Assert.ThrowsExactly<InvalidOperationException>(() => SpitzeRules.Collect(collected, collected.Revision));
        Assert.ThrowsExactly<InvalidOperationException>(() => SpitzeRules.Spin(collected, collected.Revision, SpitzeSymbol.Knife));
        Assert.ThrowsExactly<InvalidOperationException>(() => SpitzeRules.Collect(round, round.Revision + 1));
    }

    [TestMethod]
    public void FreeSpinCannotBeRequestedWithoutActionGames()
    {
        var round = SpitzeRules.Start(100, SpitzeSymbol.Gold);
        Assert.ThrowsExactly<InvalidOperationException>(() => SpitzeRules.FreeSpin(round, round.Revision, SpitzeSymbol.Knife));
    }

    [TestMethod]
    public void HighStakesExposureRemainsFiniteAndStateSurvivesJsonRoundtrip()
    {
        var round = SpitzeRules.Start(100_000, SpitzeSymbol.Blank);
        var saved = JsonSerializer.Deserialize<SpitzeRoundState>(JsonSerializer.Serialize(round));
        Assert.AreEqual(round, saved);
        Assert.AreEqual(3_628_800_000L, SpitzeRules.MaximumExposure(100_000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SpitzeRules.Start(0, SpitzeSymbol.Gold));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SpitzeRules.Start(1_000_050, SpitzeSymbol.Gold));
    }

    [TestMethod]
    public void FirstSpinCollectionHasPinnedHouseEdge()
    {
        long allOutcomes = 0;
        for (var roll = 0; roll < 100; roll++)
        {
            var state = SpitzeRules.Start(100, SpitzeRules.FromRoll(roll));
            allOutcomes += SpitzeRules.Collect(state, state.Revision).FinalPayout!.Value;
        }
        Assert.AreEqual(4_200L, allOutcomes);
        Assert.IsTrue(allOutcomes < 100 * 100);
    }

    [TestMethod]
    public void SeededFullRoundsStayWithinReasonableSimulatedReturnBounds()
    {
        // This is a regression probe for a fixed play policy, not certified RTP.
        var random = new Random(531);
        long paid = 0;
        long settled = 0;
        for (var i = 0; i < 10_000; i++)
        {
            var state = SpitzeRules.Start(100, SpitzeRules.FromRoll(random.Next(100)));
            while (state.Status == SpitzeRoundStatus.Active)
            {
                state = state.ActionGamesRemaining > 0
                    ? SpitzeRules.FreeSpin(state, state.Revision, SpitzeRules.FromRoll(random.Next(100)))
                    : SpitzeRules.Spin(state, state.Revision, SpitzeRules.FromRoll(random.Next(100)));
            }
            paid += (long)state.PaidSpins * state.Stake;
            settled += state.FinalPayout!.Value;
        }
        var ratio = (double)settled / paid;
        Assert.IsTrue(ratio is > 0.7 and < 1.05, $"Simulated return out of bounds: {ratio:P2}");
    }

    [TestMethod]
    public void OneHundredPaidSpinsSettleWithoutAnExtraDebit()
    {
        var round = SpitzeRules.Start(50, SpitzeSymbol.Blank);
        for (var i = 1; i < SpitzeRules.MaximumPaidSpins; i++)
            round = SpitzeRules.Spin(round, round.Revision, SpitzeSymbol.Blank);
        Assert.AreEqual(100, round.PaidSpins);
        Assert.AreEqual(SpitzeRoundStatus.Completed, round.Status);
        Assert.AreEqual(0L, round.FinalPayout);
        Assert.ThrowsExactly<InvalidOperationException>(() => SpitzeRules.Spin(round, round.Revision, SpitzeSymbol.Blank));
    }
}
