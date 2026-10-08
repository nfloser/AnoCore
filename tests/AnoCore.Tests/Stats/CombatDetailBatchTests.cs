using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class CombatDetailBatchTests
{
    private static readonly PlayerId Player = new(76561198000012202);

    [TestMethod]
    public void BatchCopiesInputsAndRetainsEventPayloads()
    {
        var fire = Fire();
        var input = new[] { fire };
        var batch = new CombatDetailBatch(input, []);
        input[0] = Fire();
        Assert.AreSame(fire, batch.WeaponFire.Single());
        Assert.AreEqual(1, batch.Count);
    }

    [TestMethod]
    public void BatchRejectsOversizedAndNullEntries()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new CombatDetailBatch(Enumerable.Range(0, 257).Select(_ => Fire()), []));
        Assert.ThrowsExactly<ArgumentException>(() => new CombatDetailBatch([null!], []));
        Assert.AreEqual(0, new CombatDetailBatch([], []).Count);
    }

    internal static CombatWeaponFireEvent Fire()
        => new(Guid.NewGuid(), Player, DateTimeOffset.UtcNow, "de_dust2", "ak47");
}
