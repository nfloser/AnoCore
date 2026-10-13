using AnoCore.Modules.Arcade.Casino;
namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class CasinoRulesTests
{
    [TestMethod]
    public void SlotsExpectedReturnIsPinnedToVersionedRules()
    {
        Assert.IsTrue(Math.Abs(CasinoRules.SlotsExpectedReturn - 0.934585259885204m) < 0.000000000001m);
    }

    [TestMethod]
    public void CoinflipBothSidesPayExactlyTwiceStake()
    {
        foreach (var choice in new[] { 0, 1 })
        {
            Assert.AreEqual(200L, CasinoRules.EvaluateNumber(new(CasinoGame.Coinflip, 100, choice), choice).Payout);
            Assert.AreEqual(0L, CasinoRules.EvaluateNumber(new(CasinoGame.Coinflip, 100, choice), 1 - choice).Payout);
        }
    }
    [TestMethod]
    public void RouletteZeroLosesOutsideBets_AndStraightPays36Times()
    {
        foreach (var choice in new[] { 37, 38, 39 }) Assert.AreEqual(0L, CasinoRules.EvaluateNumber(new(CasinoGame.Roulette, 100, choice), 0).Payout);
        for (var number = 0; number <= 36; number++) Assert.AreEqual(3600L, CasinoRules.EvaluateNumber(new(CasinoGame.Roulette, 100, number), number).Payout);
        Assert.AreEqual(18, Enumerable.Range(0, 37).Count(n => CasinoRules.EvaluateNumber(new(CasinoGame.Roulette, 100, 37), n).Won));
        Assert.AreEqual(18, Enumerable.Range(0, 37).Count(n => CasinoRules.EvaluateNumber(new(CasinoGame.Roulette, 100, 38), n).Won));
    }
    [TestMethod]
    public void SlotsPayFiveLinesWithSplitStake_AndCopyOutcomeGrid()
    {
        var grid = Enumerable.Repeat(7, 9).ToArray();
        var result = CasinoRules.EvaluateSlots(100, grid);
        Assert.AreEqual(100000L, result.Payout);
        grid[0] = 0;
        Assert.AreEqual(7, result.Symbols[0]);
        Assert.AreEqual(0L, CasinoRules.EvaluateSlots(100, [0, 1, 2, 3, 4, 5, 6, 7, 0]).Payout);
    }
    [TestMethod]
    public void InvalidStakesSelectionsAndResultsAreRejected()
    {
        foreach (var stake in new[] { 0, 49, 51, 501, int.MaxValue }) Assert.ThrowsExactly<ArgumentException>(() => CasinoRules.Draw(new(CasinoGame.Slots, stake, 0)));
        Assert.ThrowsExactly<ArgumentException>(() => CasinoRules.Draw(new(CasinoGame.Coinflip, 100, 2)));
        Assert.ThrowsExactly<ArgumentException>(() => CasinoRules.EvaluateNumber(new(CasinoGame.Roulette, 100, 0), 37));
    }
}
