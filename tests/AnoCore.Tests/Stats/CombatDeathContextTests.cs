using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class CombatDeathContextTests
{
    [TestMethod]
    public void ContextIsImmutableBoundedAndLegacyDeathsRemainValid()
    {
        var context = new CombatDeathContext("de_mirage", "awp", PlayerTeam.CounterTerrorist,
            true, true, true, 2, 35.123456m, attackerBlind: true);
        Assert.AreEqual(35.1235m, context.DistanceMeters);
        Assert.AreEqual("awp", context.Weapon);
        Assert.IsTrue(context.AttackerBlind);
        Assert.IsNull(new CombatDeath(Guid.NewGuid(), new(1), new(2), null, DateTimeOffset.UtcNow).Context);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CombatDeathContext("de_mirage", "awp",
            PlayerTeam.Unknown, false, false, false, -1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CombatDeathContext("de_mirage", "awp",
            PlayerTeam.Unknown, false, false, false, 0, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CombatDeathContext("de_mirage", "awp",
            (PlayerTeam)99, false, false, false, 0, 0));
    }
}
