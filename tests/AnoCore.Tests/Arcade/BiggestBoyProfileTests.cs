using System.Text.Json;
using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class BiggestBoyProfileTests
{

    [TestMethod]
    public void NumericLuckRejectsInvalidValuesAndSupportsEveryPercentage()
    {
        foreach (var invalid in new[] { "lucky:-1", "lucky:101", "lucky:1.5", "lucky:001", "lucky:abc", "lucky:" })
            Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyProfiles.Validate(invalid));
        for (var percent = 0; percent <= 100; percent++)
        {
            var profile = BiggestBoyProfiles.Validate("lucky:" + percent);
            Assert.AreEqual(percent, BiggestBoyProfiles.LuckyPercent(profile));
        }
        Assert.AreEqual(0, BiggestBoyProfiles.LuckyPercent(BiggestBoyProfiles.Standard));
    }

    [TestMethod]
    public void MaximumLuckGivesARealPaidLineAndThreeScattersOnEverySpin()
    {
        foreach (var roll in new[] { 0, 1, 42, 99 })
        {
            var spin = BiggestBoyExpandingRules.StartRound(100, bound => Math.Min(bound - 1, roll), "lucky:100");
            Assert.AreEqual("lucky:100", spin.State.Profile);
            Assert.AreEqual(3, spin.ScatterCount);
            Assert.IsTrue(spin.LineWins.Any(w => w.Payline == 2 && w.MatchCount == 5 && w.Symbol == BiggestBoySymbol.O));
            Assert.IsTrue(spin.Payout > 0);
            Assert.AreEqual(10, spin.State.FreeSpinsRemaining);
            Assert.AreEqual(5, spin.Stops.Length);
            for (var reel = 0; reel < 5; reel++)
                for (var row = 0; row < 3; row++)
                    Assert.AreEqual(BiggestBoyExpandingRules.ReelStrips[reel][(spin.Stops[reel] + row) % 100],
                        spin.Grid[reel * 3 + row]);
            var next = BiggestBoyExpandingRules.PlayFreeSpin(spin.State, bound => Math.Min(bound - 1, roll));
            Assert.AreEqual(0, next.ChargedStake);
            Assert.AreEqual("lucky:100", next.State.Profile);
            Assert.IsTrue(next.State.TotalPayout >= spin.State.TotalPayout);
            Assert.AreEqual(3, next.ScatterCount);
        }
    }

    [TestMethod]
    public void LuckyPercentageControlsTheSpecialDrawWithoutASecondClientRoll()
    {
        Assert.AreEqual(3, BiggestBoyExpandingRules.StartRound(100, _ => 0, "lucky:1").ScatterCount);
        Assert.AreEqual(3, BiggestBoyExpandingRules.StartRound(100, bound => Math.Min(bound - 1, 49), "lucky:50").ScatterCount);
        Assert.AreEqual("lucky:50", BiggestBoyExpandingRules.StartRound(100, bound => bound - 1, "lucky:50").State.Profile);
    }

    [TestMethod]
    public void UnknownProfilesAreRejectedAndOldSnapshotsDefaultToStandard()
    {
        Assert.ThrowsExactly<ArgumentException>(() => BiggestBoyProfiles.Validate("typo"));
        var state = JsonSerializer.Deserialize<BiggestBoyRoundState>("{\"LockedStake\":100}")!;
        Assert.AreEqual("standard", state.Profile);
    }

    [TestMethod]
    public void EveryWeightedTicketMapsToAnUnchangedPhysicalStop()
    {
        foreach (var profile in BiggestBoyProfiles.Names)
            for (var reel = 0; reel < 5; reel++)
            {
                var total = Enumerable.Range(0, 100).Sum(stop => BiggestBoyProfiles.StopWeight(profile, reel, stop));
                var counts = new int[100];
                for (var ticket = 0; ticket < total; ticket++) counts[BiggestBoyProfiles.SelectStop(profile, reel, _ => ticket)]++;
                for (var stop = 0; stop < 100; stop++) Assert.AreEqual(BiggestBoyProfiles.StopWeight(profile, reel, stop), counts[stop]);
            }
    }

    [TestMethod]
    public void ProfileIsStoredAndUsedForTheWholeBonusAfterSerialization()
    {
        foreach (var profile in BiggestBoyProfiles.Names)
        {
            var spin = BiggestBoyExpandingRules.StartRound(100, _ => 0, profile);
            Assert.AreEqual(profile, spin.State.Profile);
            Assert.AreEqual(20, spin.State.FreeSpinsRemaining);
            var state = JsonSerializer.Deserialize<BiggestBoyRoundState>(JsonSerializer.Serialize(spin.State))!;
            var next = BiggestBoyExpandingRules.PlayFreeSpin(state, _ => 0);
            Assert.AreEqual(profile, next.State.Profile);
            Assert.AreEqual(0, next.ChargedStake);
            Assert.AreEqual(spin.State.TotalPayout + next.Payout, next.State.TotalPayout);
        }
    }
}
