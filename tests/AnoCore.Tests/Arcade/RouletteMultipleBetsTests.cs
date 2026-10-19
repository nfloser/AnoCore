using System.Text.Json;
using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class RouletteMultipleBetsTests
{
    [TestMethod]
    public void AllWagersUseExactlyOneDrawAndPayTogether()
    {
        var request = new CasinoRequest(CasinoGame.Roulette, 300, 0,
            [new(7, 100), new(37, 150), new(43, 50)]);
        var draws = 0;
        var result = CasinoRules.Draw(request, max => { draws++; Assert.AreEqual(37, max); return 7; });
        Assert.AreEqual(1, draws);
        Assert.AreEqual(4050L, result.Payout);
        Assert.AreEqual(4050L, CasinoRules.MaximumPayout(request));
        Assert.AreEqual(0L, CasinoRules.EvaluateNumber(request, 0).Payout);
        Assert.AreEqual(150L, CasinoRules.EvaluateNumber(request, 2).Payout);
    }

    [TestMethod]
    public void ExposureIsMaximumOfSharedOutcomesRatherThanSumOfIndependentWins()
    {
        var request = new CasinoRequest(CasinoGame.Roulette, 100, 0, [new(37, 50), new(38, 50)]);
        Assert.AreEqual(100L, CasinoRules.MaximumPayout(request));
        foreach (var number in Enumerable.Range(0, 37))
            Assert.IsTrue(CasinoRules.EvaluateNumber(request, number).Payout <= CasinoRules.MaximumPayout(request));
    }

    [TestMethod]
    public void EveryBetSettlesAgainstTheSamePocketAndExposureBoundsAllPockets()
    {
        var bets = Enumerable.Range(0, 49).Select(choice => new RouletteBet(choice, 50)).ToArray();
        var request = new CasinoRequest(CasinoGame.Roulette, 2450, 0, bets);
        var maximum = 0L;
        foreach (var number in Enumerable.Range(0, 37))
        {
            var expected = bets.Sum(bet => CasinoRules.EvaluateNumber(new(CasinoGame.Roulette, bet.Stake, bet.Choice), number).Payout);
            Assert.AreEqual(expected, CasinoRules.EvaluateNumber(request, number).Payout);
            maximum = Math.Max(maximum, expected);
        }
        Assert.AreEqual(maximum, CasinoRules.MaximumPayout(request));
    }

    [TestMethod]
    public void InvalidOrMismatchedBasketsAreRejected()
    {
        CasinoRequest[] requests = [
            new(CasinoGame.Roulette, 100, 0, []),
            new(CasinoGame.Roulette, 100, 0, [new(7, 50)]),
            new(CasinoGame.Roulette, 100, 0, [new(7, 50), new(7, 50)]),
            new(CasinoGame.Roulette, 100, 0, [new(49, 100)]),
            new(CasinoGame.Roulette, 100, 0, [new(7, 51), new(8, 49)]),
            new(CasinoGame.Coinflip, 100, 0, [new(0, 100)])
        ];
        foreach (var request in requests)
            Assert.ThrowsExactly<ArgumentException>(() => CasinoRules.Draw(request));
    }

    [TestMethod]
    public void LegacyRequestJsonStaysByteCompatibleAndBasketRoundTrips()
    {
        var legacy = new CasinoRequest(CasinoGame.Roulette, 100, 7);
        Assert.AreEqual("{\"Game\":2,\"Stake\":100,\"Choice\":7}", JsonSerializer.Serialize(legacy));
        var basket = legacy with { Bets = [new(7, 100)] };
        var restored = JsonSerializer.Deserialize<CasinoRequest>(JsonSerializer.Serialize(basket))!;
        Assert.AreEqual(3600L, CasinoRules.EvaluateNumber(restored, 7).Payout);
    }

    [TestMethod]
    public void ConfiguredCeilingAppliesToTheWholeBasket()
    {
        var limits = new CasinoStakeLimits { Roulette = 100 };
        Assert.ThrowsExactly<CasinoRejectedException>(() => limits.Check(
            new(CasinoGame.Roulette, 150, 0, [new(7, 100), new(37, 50)])));
    }
}
