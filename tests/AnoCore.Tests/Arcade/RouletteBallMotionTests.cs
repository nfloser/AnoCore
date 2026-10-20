using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class RouletteBallMotionTests
{
    [TestMethod]
    public void AllPocketsLandInTheirExactStationaryWheelSegmentFromEveryStart()
    {
        CollectionAssert.AreEquivalent(Enumerable.Range(0, 37).ToArray(), RouletteBallMotion.PocketOrder.ToArray());
        for (var number = 0; number <= 36; number++)
            for (var start = 0; start < RouletteBallMotion.Angles; start++)
            {
                Assert.AreEqual(start, RouletteBallMotion.FrameAt(number, TimeSpan.Zero, start));
                var target = RouletteBallMotion.LandingFrame(number);
                Assert.AreEqual(5 * 148 + RouletteBallMotion.PocketOrder.ToList().IndexOf(number) * 4, target);
                Assert.AreEqual(target, RouletteBallMotion.FrameAt(number, TimeSpan.FromMilliseconds(3600), start));
                Assert.AreEqual(target, RouletteBallMotion.FrameAt(number, TimeSpan.FromHours(1), start));
            }
    }

    [TestMethod]
    public void TrajectoryIsBoundedAndProgressesWithoutResettingBeforeLanding()
    {
        for (var number = 0; number <= 36; number++)
        {
            var previousAngle = 0;
            var previousRing = 0;
            var distance = 0;
            for (var milliseconds = 0; milliseconds <= 3600; milliseconds += 50)
            {
                var frame = RouletteBallMotion.FrameAt(number, TimeSpan.FromMilliseconds(milliseconds));
                Assert.IsTrue(frame >= 0 && frame < RouletteBallMotion.FrameCount);
                var angle = frame % 148;
                var ring = frame / 148;
                var step = (angle - previousAngle + 148) % 148;
                Assert.IsTrue(step <= 13, "A 50ms update must not jump more than a small arc.");
                Assert.IsTrue(ring >= previousRing);
                distance += step;
                previousAngle = angle;
                previousRing = ring;
            }
            Assert.IsTrue(distance >= 2 * 148);
            Assert.AreEqual(5, previousRing);
        }
    }

    [TestMethod]
    public void InvalidInputsAreRejectedAndPreparationDoesNotMove()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RouletteBallMotion.LandingFrame(37));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RouletteBallMotion.FrameAt(0, TimeSpan.Zero, 148));
        Assert.AreEqual(20, RouletteBallMotion.FrameAt(0, TimeSpan.FromMilliseconds(199), 20));
    }
}
