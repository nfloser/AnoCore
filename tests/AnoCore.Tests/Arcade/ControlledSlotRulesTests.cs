using System.Text.Json;
using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class ControlledSlotRulesTests
{
    [TestMethod]
    public void NewPaytableReducesPayoutsAndPreservesLegacyBonusRules()
    {
        var grid = Enumerable.Repeat(BiggestBoySymbol.Scatter, 15).ToArray();
        grid[0] = grid[3] = grid[6] = BiggestBoySymbol.Kebap;
        Assert.AreEqual(50L, BiggestBoyBalancedRules.EvaluateGrid(100, grid).Payout);
        Assert.AreEqual(2700L, BiggestBoyExpandingRules.EvaluateGrid(100, grid).Payout);
        var spin = ControlledSlotRules.Biggest(100, _ => 0, new(100, 0, 100));
        Assert.AreEqual(5000L, spin.Payout);
        Assert.AreEqual("anocore-biggest-boy-controlled-v6", spin.State.RulesVersion);
        var legacy = spin.State with
        {
            RulesVersion = "anocore-biggest-boy-controlled-v5", FreeSpinsRemaining = 10,
            FreeSpinsAwarded = 10, ExpandingSymbol = BiggestBoySymbol.Kebap,
            TotalPayout = 0, OutcomeSettings = new(100, 0, 0)
        };
        var resumed = ControlledSlotRules.Biggest(100, _ => 0, new(0, 100, 0), legacy);
        Assert.AreEqual(300000L, resumed.Payout);
        Assert.AreEqual(legacy.RulesVersion, resumed.State.RulesVersion);
        Assert.AreEqual(0, resumed.ChargedStake);
    }

    [TestMethod]
    public void FruitPaytableAndHistoricalBonusKeepTheirOwnMultipliers()
    {
        var grid = Enumerable.Repeat(BonusSlotSymbol.Seven, 15).ToArray();
        Assert.AreEqual(5000L, BookBalancedRules.EvaluateGrid(100, grid).Payout);
        Assert.AreEqual(20000L, BonusSlotRules.EvaluateGrid(100, grid).Payout);
        var first = ControlledSlotRules.Book(100, _ => 0, new(100, 100, 0));
        var legacy = first.State with
        {
            RulesVersion = ControlledSlotRules.LegacyBookVersion, TotalPayout = 0,
            ExpandingSymbol = BonusSlotSymbol.Seven, OutcomeSettings = new(100, 0, 0)
        };
        int Seven(int bound) => bound == 7 ? 6 : 0;
        var resumed = ControlledSlotRules.Book(100, Seven, new(0, 100, 0), legacy);
        Assert.AreEqual(20000L, resumed.Payout);
        Assert.AreEqual(legacy.RulesVersion, resumed.State.RulesVersion);
        Assert.AreEqual(0, resumed.ChargedStake);
        Assert.AreEqual(155000000L, BookBalancedRules.MaximumRoundExposure(100000));
        Assert.AreEqual(155000000L, BiggestBoyBalancedRules.MaximumRoundExposure(100000));
    }

    [TestMethod]
    public void OrdinaryWinningFallbackDoesNotForceAFullScreen()
    {
        var draws = 0;
        int Random(int bound) => bound == 100 ? 99 : bound == 3 ? 0 : draws++ % bound;
        var spin = ControlledSlotRules.Biggest(100, Random, new(100, 0, 0));
        Assert.IsTrue(spin.Payout > 100);
        Assert.IsTrue(spin.RawGrid.Distinct().Count() > 1);
        draws = 0;
        var fruit = ControlledSlotRules.Book(100, Random, new(100, 0, 0));
        Assert.IsTrue(fruit.Payout > 100);
        Assert.IsTrue(fruit.Grid.Distinct().Count() > 1);
    }

    [TestMethod]
    public void IndependentBoundaryProbabilitiesMatchActualPaidResults()
    {
        foreach (var win in new[] { 0, 100 })
            foreach (var bonus in new[] { 0, 100 })
                foreach (var seed in Enumerable.Range(0, 25))
                {
                    var rng = new Random(seed);
                    var settings = new SlotOutcomeSettings(win, bonus, 0);
                    var biggest = ControlledSlotRules.Biggest(100, rng.Next, settings);
                    Assert.AreEqual(win == 100, biggest.Payout > 100);
                    Assert.AreEqual(bonus == 100, biggest.State.FreeSpinsRemaining > 0);
                    Assert.AreEqual(biggest.Payout, biggest.LineWins.Sum(x => x.Payout));
                    Assert.AreEqual(bonus == 100, biggest.RawGrid.Count(x => x == BiggestBoySymbol.Scatter) >= 3);
                    var book = ControlledSlotRules.Book(100, rng.Next, settings);
                    Assert.AreEqual(win == 100, book.Payout > 100);
                    Assert.AreEqual(bonus == 100, book.State.FreeSpinsRemaining > 0);
                    Assert.AreEqual(book.Payout, book.LineWins.Sum(x => x.Payout));
                }
    }

    [TestMethod]
    public void FullScreensVaryAcrossRegularSymbolsAndPayAllLines()
    {
        var biggestSymbols = new HashSet<BiggestBoySymbol>();
        var bookSymbols = new HashSet<BonusSlotSymbol>();
        var rng = new Random(509);
        for (var i = 0; i < 300; i++)
        {
            var settings = new SlotOutcomeSettings(100, 0, 100);
            var biggest = ControlledSlotRules.Biggest(100, rng.Next, settings);
            Assert.AreEqual(1, biggest.Grid.Distinct().Count());
            Assert.AreEqual(10, biggest.LineWins.Length);
            Assert.AreEqual(100L * BiggestBoyBalancedRules.Multiplier(biggest.Grid[0], 5), biggest.Payout);
            biggestSymbols.Add(biggest.Grid[0]);
            var book = ControlledSlotRules.Book(100, rng.Next, settings);
            Assert.AreEqual(1, book.Grid.Distinct().Count());
            Assert.AreEqual(10, book.LineWins.Length);
            bookSymbols.Add(book.Grid[0]);
        }
        Assert.AreEqual(10, biggestSymbols.Count);
        Assert.AreEqual(7, bookSymbols.Count);
    }

    [TestMethod]
    public void SettingsSurviveSerializationAndBonusCapChargesOnlyOnce()
    {
        var settings = new SlotOutcomeSettings(0, 100, 0);
        var rng = new Random(77);
        var biggest = ControlledSlotRules.Biggest(100, rng.Next, settings);
        var book = ControlledSlotRules.Book(100, rng.Next, settings);
        Assert.AreEqual(100, biggest.ChargedStake);
        Assert.AreEqual(100, book.ChargedStake);
        for (var i = 0; i < 30; i++)
        {
            var saved = JsonSerializer.Deserialize<BiggestBoyRoundState>(JsonSerializer.Serialize(biggest.State))!;
            biggest = ControlledSlotRules.Biggest(100, rng.Next, new(100, 0, 100), saved);
            var savedBook = JsonSerializer.Deserialize<BonusSlotRoundState>(JsonSerializer.Serialize(book.State))!;
            book = ControlledSlotRules.Book(100, rng.Next, new(100, 0, 100), savedBook);
            Assert.AreEqual(0L, biggest.Payout);
            Assert.AreEqual(0L, book.Payout);
            Assert.AreEqual(settings, biggest.State.OutcomeSettings);
            Assert.AreEqual(settings, book.State.OutcomeSettings);
            Assert.AreEqual(0, biggest.ChargedStake);
            Assert.AreEqual(0, book.ChargedStake);
        }
        Assert.AreEqual(0, biggest.State.FreeSpinsRemaining);
        Assert.AreEqual(0, book.State.FreeSpinsRemaining);
        Assert.AreEqual(30, biggest.State.FreeSpinsAwarded);
        Assert.AreEqual(30, book.State.FreeSpinsAwarded);
    }

    [TestMethod]
    public void AdversarialRandomSourceTerminatesAndInvalidSettingsReject()
    {
        foreach (var win in new[] { 0, 100 })
            foreach (var bonus in new[] { 0, 100 })
                foreach (var value in new[] { 0, 99 })
                {
                    int Rng(int bound) => Math.Min(value, bound - 1);
                    Assert.AreEqual(win == 100, ControlledSlotRules.Biggest(100, Rng, new(win, bonus)).Payout > 100);
                    Assert.AreEqual(win == 100, ControlledSlotRules.Book(100, Rng, new(win, bonus)).Payout > 100);
                }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SlotOutcomeSettings(-1).Validate());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SlotOutcomeSettings(1, 101).Validate());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SlotOutcomeSettings(1, 1, 101).Validate());
    }

    [TestMethod]
    public void EmpiricalPaidWinAndBonusFrequenciesAreIndependent()
    {
        var random = new Random(20261015);
        var wins = 0; var bonuses = 0; var both = 0;
        const int count = 10000;
        for (var i = 0; i < count; i++)
        {
            var spin = ControlledSlotRules.Biggest(100, random.Next, new(30, 20, 2));
            var won = spin.Payout > 100;
            var bonus = spin.State.FreeSpinsRemaining > 0;
            if (won) wins++;
            if (bonus) bonuses++;
            if (won && bonus) both++;
        }
        Assert.IsTrue(wins is > 2800 and < 3200, $"Wins: {wins}");
        Assert.IsTrue(bonuses is > 1800 and < 2200, $"Bonuses: {bonuses}");
        Assert.IsTrue(both is > 480 and < 720, $"Joint outcomes: {both}");
    }
}
