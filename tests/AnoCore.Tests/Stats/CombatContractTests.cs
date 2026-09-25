using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class CombatContractTests
{
    private static readonly PlayerId Victim = new(76561198000012101);
    private static readonly PlayerId Attacker = new(76561198000012102);
    private static readonly PlayerId Assister = new(76561198000012103);
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Death_RejectsEmptyIdentityAndNormalizesInvalidAssist()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new CombatDeath(Guid.Empty, Victim, Attacker, Assister, Now));
        var suicide = new CombatDeath(Guid.NewGuid(), Victim, Victim, Victim, Now);
        Assert.IsNull(suicide.AttackerId);
        Assert.IsNull(suicide.AssisterId);
        var teamKill = new CombatDeath(Guid.NewGuid(), Victim, Attacker, Assister, Now, isTeamKill: true);
        Assert.IsTrue(teamKill.IsTeamKill);
        Assert.IsNull(teamKill.AssisterId);
        var duplicateAssist = new CombatDeath(Guid.NewGuid(), Victim, Attacker, Attacker, Now);
        Assert.IsNull(duplicateAssist.AssisterId);
    }

    [TestMethod]
    public void Death_NormalizesTimestampToUtc()
    {
        var local = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.FromHours(2));
        var death = new CombatDeath(Guid.NewGuid(), Victim, Attacker, Assister, local);
        Assert.AreEqual(Now, death.OccurredAtUtc);
    }
}
