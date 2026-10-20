using AnoCore.Modules.Arcade.Casino;
namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class CasinoStakeLimitsTests
{
    [TestMethod]
    public void FruitStepsRespectConfiguredCeilingsAndKeepOtherGameDefaults()
    {
        Assert.AreEqual(10000, new CasinoStakeLimits().Book);
        foreach (var maximum in new[] { 50, 150, 750, 1050, 10000, 100000, 1000000 })
        {
            var limits = new CasinoStakeLimits { Book = maximum };
            var stake = 50;
            for (var i = 0; i < 20; i++) stake = limits.StepBookStake(stake, 1);
            Assert.AreEqual(maximum, stake);
            for (var i = 0; i < 20; i++) stake = limits.StepBookStake(stake, -1);
            Assert.AreEqual(50, stake);
            Assert.AreEqual(maximum, limits.StepBookStake(maximum + 50, 0));
        }
        var configured = new CasinoStakeLimits { Book = 100000 };
        Assert.AreEqual(250, configured.StepBookStake(100, 1));
        Assert.AreEqual(10000, configured.StepBookStake(25000, -1));
    }

    [TestMethod]
    public void HighFruitStakeUsesExactLongPayoutAndConservativeBonusExposure()
    {
        var grid = Enumerable.Repeat(BonusSlotSymbol.Seven, 15).ToArray();
        var outcome = BonusSlotRules.EvaluateGrid(100000, grid);
        Assert.AreEqual(20000000L, outcome.Payout);
        Assert.AreEqual(outcome.Payout, outcome.LineWins.Sum(x => x.Payout));
        Assert.AreEqual(620000000L, BonusSlotRules.MaximumRoundExposure(100000));
        Assert.AreEqual(6200000000L, BonusSlotRules.MaximumRoundExposure(1000000));
    }

    [TestMethod]
    public void LimitsAreIndependentAndDefaultsRemainCompatible()
    {
        var defaults = new CasinoStakeLimits().Validate();
        foreach (var game in Enum.GetValues<CasinoGame>()) Assert.AreEqual(500, defaults.For(game));
        var limits = new CasinoStakeLimits { Coinflip = 10000, Slots = 1000, Roulette = 250, Blackjack = 2000, Risk = 5000, Book = 10000 }.Validate();
        limits.Check(new(CasinoGame.Coinflip, 10000, 0));
        Assert.ThrowsExactly<CasinoRejectedException>(() => limits.Check(new(CasinoGame.Roulette, 300, 0)));
        Assert.AreEqual(2000, limits.For(CasinoRoundGame.Blackjack));
        foreach (var invalid in new[] { 0, 49, 51, 1000050, int.MaxValue })
            Assert.ThrowsExactly<ArgumentException>(() => new CasinoStakeLimits { Book = invalid }.Validate());
    }
    [TestMethod]
    public void LargerStakesPreserveExactPayoutsAndExposureBounds()
    {
        Assert.AreEqual(20000L, CasinoRules.EvaluateNumber(new(CasinoGame.Coinflip, 10000, 0), 0).Payout);
        Assert.AreEqual(360000L, CasinoRules.MaximumPayout(new(CasinoGame.Roulette, 10000, 0)));
        Assert.AreEqual(10000000L, CasinoRules.EvaluateSlots(10000, Enumerable.Repeat(7, 9).ToArray()).Payout);
        Assert.ThrowsExactly<ArgumentException>(() => CasinoRules.Validate(new(CasinoGame.Coinflip, 1000050, 0)));
    }
}
