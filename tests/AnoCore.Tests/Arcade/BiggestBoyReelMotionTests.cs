using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class BiggestBoyReelMotionTests
{
    [TestMethod]
    public void AllStopsStayOnTheCyclicPhysicalStripWithFourVisibleCells()
    {
        for (var previous = 0; previous < 100; previous++)
            for (var target = 0; target < 100; target++)
            {
                var distance = BiggestBoyReelMotion.Steps(previous, target);
                Assert.IsTrue(distance is >= 30 and <= 129);
                for (var reel = 0; reel < 5; reel++)
                {
                    var prior = previous;
                    for (var travelled = 0; travelled <= distance; travelled++)
                    {
                        var first = BiggestBoyReelMotion.VisibleIndex(previous, travelled, 0);
                        Assert.AreEqual((previous - travelled + 100 * 2) % 100, first);
                        if (travelled > 0) Assert.AreEqual((prior + 99) % 100, first);
                        prior = first;
                        for (var row = 0; row < 4; row++)
                            Assert.AreEqual(
                                BiggestBoyExpandingRules.ReelStrips[reel][(first + row) % 100],
                                BiggestBoyExpandingRules.ReelStrips[reel][BiggestBoyReelMotion.VisibleIndex(previous, travelled, row)]);
                    }
                    Assert.AreEqual(target, BiggestBoyReelMotion.VisibleIndex(previous, distance, 0));
                    Assert.AreEqual(0, BiggestBoyReelMotion.Travelled(previous, target, -1, reel));
                    Assert.AreEqual(distance, BiggestBoyReelMotion.Travelled(previous, target, 100, reel));
                    var halfway = BiggestBoyReelMotion.Travelled(previous, target, BiggestBoyReelMotion.Duration(reel) / 2, reel);
                    Assert.IsTrue(halfway >= 0 && halfway <= distance);
                }
            }
        for (var reel = 1; reel < 5; reel++)
            Assert.AreEqual(.2, BiggestBoyReelMotion.Duration(reel) - BiggestBoyReelMotion.Duration(reel - 1), .00001);
    }
}
