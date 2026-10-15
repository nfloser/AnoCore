using AnoCore.Modules.Arcade.Casino;
namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class CasinoStakeLimitsTests
{
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
