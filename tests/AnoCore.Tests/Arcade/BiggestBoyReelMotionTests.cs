using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class BiggestBoyReelMotionTests
{
    [TestMethod]
    public void EveryPreviousAndBookedStopLandsOnTheAuthoritativeThreeSymbolWindow()
    {
        for (var previous = 0; previous < 100; previous++)
            for (var target = 0; target < 100; target++)
            {
                var steps = BiggestBoyReelMotion.Steps(previous, target);
                Assert.IsTrue(steps is >= 30 and <= 129);
                var final = BiggestBoyReelMotion.FinalVisibleIndex(previous, steps);
                Assert.AreEqual(target, final % 100);
                Assert.IsTrue(final >= 0 && 200 + previous + 2 < BiggestBoyReelMotion.RenderedCells);
                for (var reel = 0; reel < 5; reel++)
                    for (var row = 0; row < 3; row++)
                        Assert.AreEqual(BiggestBoyExpandingRules.ReelStrips[reel][(target + row) % 100],
                            BiggestBoyExpandingRules.ReelStrips[reel][(final + row) % 100]);
            }
        for (var reel = 1; reel < 5; reel++) Assert.AreEqual(.2, BiggestBoyReelMotion.Duration(reel) - BiggestBoyReelMotion.Duration(reel - 1), .00001);
    }
}
