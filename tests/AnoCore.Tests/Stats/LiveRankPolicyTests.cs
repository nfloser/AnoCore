using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class LiveRankPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static PlayerSnapshot Player(ulong id, PlayerTeam team) => new(new PlayerId(id),
        new PlayerSessionId(Guid.NewGuid()), "Player", true, true, team, Now, Now);
    private static readonly PlayerSnapshot Victim = Player(76561198000280101, PlayerTeam.CounterTerrorist);
    private static readonly PlayerSnapshot Attacker = Player(76561198000280102, PlayerTeam.Terrorist);
    private static readonly PlayerSnapshot Assister = Player(76561198000280103, PlayerTeam.Terrorist);
    private static RankDeathInput Death(PlayerTeam? attackerTeam = null) => new(
        new(Guid.NewGuid(), Now, false, 4, "round-1"),
        new(Victim, Victim.Team, false), new(Attacker, attackerTeam ?? Attacker.Team, false), Assister,
        "ak47", [GameplayStatKind.HeadshotKill], true, 25m);

    [TestMethod]
    public void Death_CombinesConfiguredBonusesAndPositiveVipWithoutChangingVictimPenalty()
    {
        var configuration = new RankConfiguration
        {
            GameplayPoints = new() { [GameplayStatKind.HeadshotKill] = 3, [GameplayStatKind.FlashAssist] = 2 },
            LivePolicy = new()
            {
                VipMultiplier = 2m,
                WeaponPoints = new() { ["ak47"] = 4 },
                DistanceThresholdMeters = 20,
                DistanceBonus = 5,
                StreakPoints = new() { [2] = 6 },
            },
        };
        var result = new LiveRankPolicy(configuration).Death(Death(), 100, 100, [Attacker.Id, Assister.Id], 2);
        Assert.AreEqual(40L, result.Single(award => award.PlayerId == Attacker.Id).Points);
        Assert.AreEqual(6L, result.Single(award => award.PlayerId == Assister.Id).Points);
        Assert.AreEqual(-1L, result.Single(award => award.PlayerId == Victim.Id).Points);
    }

    [TestMethod]
    public void DynamicRatios_AreBoundedAndTruncatedAndVipNeverMultipliesPenalties()
    {
        var configuration = new RankConfiguration
        {
            KillPoints = 10,
            DeathPenalty = 10,
            LivePolicy = new() { DynamicMultipliers = true, MinimumDynamicMultiplier = 0.5m, MaximumDynamicMultiplier = 2m, VipMultiplier = 3m },
        };
        var policy = new LiveRankPolicy(configuration);
        var result = policy.Death(Death(), 1000, 1, [Attacker.Id, Victim.Id], 1);
        Assert.AreEqual(60L, result.Single(award => award.PlayerId == Attacker.Id).Points);
        Assert.AreEqual(-5L, result.Single(award => award.PlayerId == Victim.Id).Points);
        result = policy.Death(Death(), 3, 2, [], 1);
        Assert.AreEqual(15L, result.Single(award => award.PlayerId == Attacker.Id).Points);
        Assert.AreEqual(-6L, result.Single(award => award.PlayerId == Victim.Id).Points);
    }

    [TestMethod]
    public void Eligibility_IsIndependentAndBotTeamkillSuicideAndFfaRulesAreExplicit()
    {
        var policy = new LiveRankPolicy(new RankConfiguration { LivePolicy = new() { MinimumPlayers = 2 } });
        Assert.IsFalse(policy.Allowed(Death().Context with { IsWarmup = true }));
        Assert.IsFalse(policy.Allowed(Death().Context with { HumanPlayers = 1 }));
        Assert.IsTrue(policy.Allowed(Death().Context));
        var teamkill = policy.Death(Death(Victim.Team), 0, 0, [Attacker.Id], 1);
        Assert.AreEqual(-2L, teamkill.Single(award => award.PlayerId == Attacker.Id).Points);
        Assert.IsFalse(teamkill.Any(award => award.PlayerId == Assister.Id));
        var suicide = policy.Death(Death() with { Attacker = null }, 0, 0, [], 1);
        Assert.AreEqual(-1L, suicide.Single().Points);
        Assert.IsEmpty(policy.Death(Death() with { Victim = new(null, PlayerTeam.CounterTerrorist, true) }, 0, 0, [], 1));
        var ffa = new LiveRankPolicy(new RankConfiguration { LivePolicy = new() { FreeForAll = true, IncludeBots = true } });
        Assert.AreEqual(2L, ffa.Death(Death(Victim.Team), 0, 0, [], 1).Single(award => award.PlayerId == Attacker.Id).Points);
        Assert.AreEqual(2L, ffa.Death(Death() with { Victim = new(null, PlayerTeam.CounterTerrorist, true) }, 0, 0, [], 1).Single(award => award.PlayerId == Attacker.Id).Points);
    }

    [TestMethod]
    public void Policy_SnapshotsConfigurationAndRejectsMalformedBounds()
    {
        var configuration = new RankConfiguration { LivePolicy = new() { WeaponPoints = new() { ["ak47"] = 3 } } };
        var policy = new LiveRankPolicy(configuration);
        configuration.LivePolicy.WeaponPoints.Clear();
        configuration.KillPoints = 1000;
        Assert.AreEqual(5L, policy.Death(Death(), 0, 0, [], 1).Single(award => award.PlayerId == Attacker.Id).Points);
        foreach (var invalid in new LiveRankConfiguration[]
        {
            new() { MinimumPlayers = 0 }, new() { VipMultiplier = 11 }, new() { MinimumDynamicMultiplier = 0 },
            new() { MaximumDynamicMultiplier = 5 }, new() { MinimumDynamicMultiplier = 2, MaximumDynamicMultiplier = 1 },
            new() { StreakWindowSeconds = 0 }, new() { DistanceThresholdMeters = -1 },
            new() { StreakPoints = new() { [1] = 5 } }, new() { WeaponPoints = new() { ["bad;key"] = 1 } },
            new() { WeaponPoints = null! }, new() { StreakPoints = null! }, new() { VipPermission = "bad" },
        })
            Assert.IsNotEmpty(RankConfiguration.Validate(new RankConfiguration { LivePolicy = invalid }));
        Assert.IsNotEmpty(RankConfiguration.Validate(new RankConfiguration { Source = (RankScoreSource)255 }));
        Assert.IsNotEmpty(RankConfiguration.Validate(new RankConfiguration { LivePolicy = null! }));
    }

    [TestMethod]
    public void ConfigurationJson_PreservesLegacyDefaultAndAcceptsExplicitNamedSource()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<RankConfiguration>("{}");
        Assert.AreEqual(RankScoreSource.DerivedStatistics, old!.Source);
        var live = System.Text.Json.JsonSerializer.Deserialize<RankConfiguration>("{\"Source\":\"EventLedger\"}");
        Assert.AreEqual(RankScoreSource.EventLedger, live!.ScoreWeights.Source);
        Assert.IsEmpty(RankConfiguration.Validate(live));
    }

    [TestMethod]
    public void Objective_UsesSignedWeightsAndFfaSuppressesTeamWinLoss()
    {
        var policy = new LiveRankPolicy(new RankConfiguration
        {
            GameplayPoints = new() { [GameplayStatKind.BombPlanted] = 5, [GameplayStatKind.HostageKilled] = -5, [GameplayStatKind.RoundWon] = 2 },
            LivePolicy = new() { VipMultiplier = 2, FreeForAll = true },
        });
        Assert.AreEqual(10L, policy.Gameplay(GameplayStatKind.BombPlanted, true));
        Assert.AreEqual(-5L, policy.Gameplay(GameplayStatKind.HostageKilled, true));
        Assert.AreEqual(0L, policy.Gameplay(GameplayStatKind.RoundWon, false));
        Assert.AreEqual(0L, policy.Gameplay(GameplayStatKind.Mvp, false));
    }
    [TestMethod]
    public void AdditionalObjectivesAndWeaponFamilies_RespectSignedVipAndFfaPolicy()
    {
        var config = new RankConfiguration
        {
            GameplayPoints = new()
            {
                [GameplayStatKind.KnifeKill] = 3,
                [GameplayStatKind.PenetratedKill] = 2,
                [GameplayStatKind.HostageHurt] = -5,
                [GameplayStatKind.BombExploded] = 5,
                [GameplayStatKind.HostagesRescuedAll] = 6,
            },
            LivePolicy = new() { VipMultiplier = 2, TeamKillAssistPenalty = 3, TeamKillFlashAssistPenalty = 2 },
        };
        var policy = new LiveRankPolicy(config);
        var death = Death() with { Weapon = "knife_karambit", Specials = [GameplayStatKind.PenetratedKill], Penetrations = 3 };
        Assert.AreEqual(22L, policy.Death(death, 0, 0, [Attacker.Id], 1).Single(award => award.PlayerId == Attacker.Id).Points);
        Assert.AreEqual(-5L, policy.Gameplay(GameplayStatKind.HostageHurt, true));
        Assert.AreEqual(10L, policy.Gameplay(GameplayStatKind.BombExploded, true));
        config.LivePolicy.FreeForAll = true;
        Assert.AreEqual(0L, new LiveRankPolicy(config).Gameplay(GameplayStatKind.BombExploded, true));
        Assert.AreEqual(0L, new LiveRankPolicy(config).Gameplay(GameplayStatKind.HostagesRescuedAll, true));
        foreach (var weapon in new[] { "hegrenade", "inferno", "flashbang", "bayonet", "taser" })
            Assert.IsNotNull(LiveRankPolicy.WeaponFamily(weapon));
        Assert.IsNull(LiveRankPolicy.WeaponFamily("ak47"));
        Assert.ThrowsExactly<ArgumentException>(() => policy.Death(death with { Penetrations = 33 }, 0, 0, [], 1));
    }

    [TestMethod]
    public void TeamkillAssists_PenalizeValidSameTeamAssisterWithoutVipOrNormalAssistBonus()
    {
        var victim = Player(76561198000280104, PlayerTeam.Terrorist);
        var input = Death() with { Victim = new(victim, victim.Team, false) };
        var policy = new LiveRankPolicy(new RankConfiguration
        {
            LivePolicy = new() { VipMultiplier = 10, TeamKillAssistPenalty = 3, TeamKillFlashAssistPenalty = 2 },
        });
        Assert.AreEqual(-5L, policy.Death(input, 0, 0, [Assister.Id], 1).Single(award => award.PlayerId == Assister.Id).Points);
        Assert.IsFalse(policy.Death(input with { Assister = victim }, 0, 0, [], 1).Any(award => award.PlayerId == Assister.Id));
    }

    [TestMethod]
    public void TeamEventIdentity_IsStableAndSeparatesServerMapTickKindTeamAndDefuser()
    {
        var id = CombatEventIdentity.CreateTeam("server", "de_test", 1, 100, GameplayStatKind.BombExploded, PlayerTeam.Terrorist);
        Assert.AreEqual(id, CombatEventIdentity.CreateTeam("server", "de_test", 1, 100, GameplayStatKind.BombExploded, PlayerTeam.Terrorist));
        Assert.AreNotEqual(id, CombatEventIdentity.CreateTeam("other", "de_test", 1, 100, GameplayStatKind.BombExploded, PlayerTeam.Terrorist));
        Assert.AreNotEqual(id, CombatEventIdentity.CreateTeam("server", "de_test", 2, 100, GameplayStatKind.BombExploded, PlayerTeam.Terrorist));
        Assert.AreNotEqual(id, CombatEventIdentity.CreateTeam("server", "de_test", 1, 101, GameplayStatKind.BombExploded, PlayerTeam.Terrorist));
        Assert.AreNotEqual(id, CombatEventIdentity.CreateTeam("server", "de_test", 1, 100, GameplayStatKind.HostagesRescuedAll, PlayerTeam.Terrorist));
        Assert.AreNotEqual(id, CombatEventIdentity.CreateTeam("server", "de_test", 1, 100, GameplayStatKind.BombExploded, PlayerTeam.CounterTerrorist));
        Assert.AreNotEqual(id, CombatEventIdentity.CreateTeam("server", "de_test", 1, 100, GameplayStatKind.BombExploded, PlayerTeam.Terrorist, Attacker.Id));
        Assert.ThrowsExactly<ArgumentException>(() => CombatEventIdentity.CreateTeam("server", "de_test", 1, 100, GameplayStatKind.Mvp, PlayerTeam.Terrorist));
    }

}
