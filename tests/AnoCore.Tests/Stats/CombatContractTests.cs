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
    [TestMethod]
    public void DamageEvent_NormalizesKeysAndPreservesSelfWorldAndTeamContext()
    {
        var team = new CombatDamageEvent(Guid.NewGuid(), Victim, Attacker, Now,
            " DE_DUST2 ", " weapon_AK47 ", 1, 34, 7, isTeamDamage: true);
        Assert.AreEqual("DE_DUST2", team.MapName);
        Assert.AreEqual("weapon_AK47", team.Weapon);
        Assert.AreEqual(1, team.Hitgroup);
        Assert.AreEqual(34, team.DamageHealth);
        Assert.AreEqual(7, team.DamageArmor);
        Assert.IsTrue(team.IsTeamDamage);

        var self = new CombatDamageEvent(Guid.NewGuid(), Victim, Victim, Now,
            "de_dust2", "hegrenade", 0, 12, 0, isTeamDamage: true);
        Assert.AreEqual(Victim, self.AttackerId);
        Assert.IsFalse(self.IsTeamDamage);

        var world = new CombatDamageEvent(Guid.NewGuid(), Victim, null, Now,
            "de_dust2", "world", 0, 20, 0, isTeamDamage: true);
        Assert.IsNull(world.AttackerId);
        Assert.IsFalse(world.IsTeamDamage);
    }

    [TestMethod]
    public void DetailEvents_RejectInvalidIdentityKeysAndRanges()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new CombatWeaponFireEvent(Guid.Empty, Attacker, Now, "de_dust2", "ak47"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new CombatWeaponFireEvent(Guid.NewGuid(), Attacker, Now, " ", "ak47"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new CombatDamageEvent(Guid.NewGuid(), Victim, Attacker, Now,
                "de_dust2", new string('w', 65), 1, 1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new CombatDamageEvent(Guid.NewGuid(), Victim, Attacker, Now,
                "de_dust2", "ak47", -1, 1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new CombatDamageEvent(Guid.NewGuid(), Victim, Attacker, Now,
                "de_dust2", "ak47", 1, -1, 0));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new CombatDetailFilter("de_dust2\nother", null));
    }

    [TestMethod]
    public void DetailFilter_NormalizesOptionalKeys()
    {
        var filter = new CombatDetailFilter(" de_nuke ", " awp ",
            includeTeamDamage: true, includeSelfDamage: true);
        Assert.AreEqual("de_nuke", filter.MapName);
        Assert.AreEqual("awp", filter.Weapon);
        Assert.IsTrue(filter.IncludeTeamDamage);
        Assert.IsTrue(filter.IncludeSelfDamage);
        Assert.IsNull(new CombatDetailFilter().MapName);
    }

}
