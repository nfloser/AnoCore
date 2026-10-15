using System.Text.Json;
using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class BiggestBoyProfileTests
{
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
