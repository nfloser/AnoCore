using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class CosmeticReelMotionTests
{
    [TestMethod]
    [DataRow(8)]
    [DataRow(12)]
    public void CosmeticFieldsAreIndependentUntilLandingAndContainEverySymbol(int symbols)
    {
        var first = CosmeticReelMotion.Create(symbols, new int[15], new Random(512).Next);
        var second = CosmeticReelMotion.Create(symbols, Enumerable.Repeat(symbols - 1, 15).ToArray(), new Random(512).Next);
        for (var reel = 0; reel < 5; reel++)
        {
            var seen = new HashSet<int>();
            var steps = CosmeticReelMotion.Steps(reel);
            for (var travelled = 0; travelled <= steps - 3; travelled++)
                for (var row = 0; row < 4; row++)
                {
                    var visible = first.VisibleSymbol(reel, travelled, row);
                    Assert.AreEqual(visible, second.VisibleSymbol(reel, travelled, row), "The early ribbon must not depend on the booked outcome.");
                    seen.Add(visible);
                }
            Assert.AreEqual(symbols, seen.Count);
            for (var row = 0; row < 3; row++)
            {
                Assert.AreEqual(0, first.VisibleSymbol(reel, steps, row));
                Assert.AreEqual(symbols - 1, second.VisibleSymbol(reel, steps, row));
            }
        }
    }

    [TestMethod]
    public void NeighborsMoveDownwardContinuouslyAndEveryLandingMatchesBookedRows()
    {
        var target = Enumerable.Range(0, 15).Select(x => x % 12).ToArray();
        var motion = CosmeticReelMotion.Create(12, target, new Random(1).Next);
        for (var reel = 0; reel < 5; reel++)
        {
            var steps = CosmeticReelMotion.Steps(reel);
            for (var travelled = 0; travelled < steps; travelled++)
                for (var row = 0; row < 3; row++)
                    Assert.AreEqual(motion.VisibleSymbol(reel, travelled, row), motion.VisibleSymbol(reel, travelled + 1, row + 1));
            for (var row = 0; row < 3; row++) Assert.AreEqual(target[reel * 3 + row], motion.VisibleSymbol(reel, steps, row));
            Assert.AreEqual(0, CosmeticReelMotion.Travelled(-1, reel));
            Assert.AreEqual(steps, CosmeticReelMotion.Travelled(BiggestBoyReelMotion.Duration(reel), reel));
            var previous = 0;
            for (var millis = 0; millis < BiggestBoyReelMotion.Duration(reel) * 1000; millis++)
            {
                var travelled = CosmeticReelMotion.Travelled(millis / 1000d, reel);
                Assert.IsTrue(travelled >= previous && travelled <= steps);
                previous = travelled;
            }
        }
    }

    [TestMethod]
    public void DifferentSpinsGetDifferentRibbonsAndInvalidInputsAreRejected()
    {
        var first = CosmeticReelMotion.Create(12, new int[15], new Random(12).Next);
        var second = CosmeticReelMotion.Create(12, new int[15], new Random(13).Next);
        Assert.IsFalse(Enumerable.Range(0, 20).Select(x => first.VisibleSymbol(0, x, 0))
            .SequenceEqual(Enumerable.Range(0, 20).Select(x => second.VisibleSymbol(0, x, 0))));
        Assert.ThrowsExactly<ArgumentException>(() => CosmeticReelMotion.Create(12, new int[14], _ => 0));
        Assert.ThrowsExactly<ArgumentException>(() => CosmeticReelMotion.Create(12, Enumerable.Repeat(12, 15).ToArray(), _ => 0));
        Assert.ThrowsExactly<ArgumentException>(() => CosmeticReelMotion.Create(12, new int[15], bound => bound));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => first.VisibleSymbol(5, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => first.VisibleSymbol(0, -1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CosmeticReelMotion.Travelled(double.NaN, 0));
    }
}
