using System.Text.Json;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Progression;
using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class ServerProfileTests
{
    private static T Read<T>(string name) => JsonSerializer.Deserialize<T>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "server-profile", name + ".json")))!;

    [TestMethod]
    public void OperatorFilesValidateAndDefineSupportedRewardsWithoutChangingRankRules()
    {
        var ranks = Read<RankConfiguration>("ranks");
        Assert.IsEmpty(RankConfiguration.Validate(ranks));
        Assert.IsEmpty(GameplayStatsConfiguration.Validate(Read<GameplayStatsConfiguration>("gameplay-stats")));
        Assert.IsEmpty(GameplayXpConfiguration.Validate(Read<GameplayXpConfiguration>("gameplay-xp")));
        Assert.IsEmpty(ProgressionConfiguration.Validate(Read<ProgressionConfiguration>("progression")));
        Assert.IsEmpty(ChallengeConfiguration.Validate(Read<ChallengeConfiguration>("challenges")));
        Assert.IsEmpty(AchievementConfiguration.ValidateCatalog(Read<AchievementConfiguration>("achievements")));
        Assert.IsEmpty(SeasonConfiguration.Validate(Read<SeasonConfiguration>("seasons")));
        Assert.AreEqual(1000L, ranks.StartingPoints);
        Assert.AreEqual(8, ranks.LivePolicy.MinimumPlayers);
        Assert.AreEqual(RankScoreSource.EventLedger, ranks.Source);
        Assert.AreEqual(18, ranks.Thresholds.Count);
        Assert.AreEqual("GOAT", ranks.ForScore(15000).Name);
        Assert.AreEqual("STARTER", ranks.ForScore(1000).Name);
        Assert.AreEqual(0, ranks.GameplayPoints[GameplayStatKind.ThroughSmokeKill]);
        Assert.AreEqual(0, ranks.LivePolicy.PlaytimeIntervalSeconds);
        Assert.AreEqual(0, ranks.LivePolicy.StreakWindowSeconds);
        Assert.AreEqual(3, ranks.LivePolicy.StreakPoints[5]);
        Assert.AreEqual(8100L, Read<ProgressionConfiguration>("progression").Levels[9].MinimumXp);
        Assert.IsFalse(Read<SeasonConfiguration>("seasons").Enabled);
    }

    [TestMethod]
    public void BackupRankAwardsKeepNormalSpecialTeamAndSuicidePoints()
    {
        var policy = new LiveRankPolicy(Read<RankConfiguration>("ranks"));
        var now = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        PlayerSnapshot Player(ulong id, PlayerTeam team) => new(new(id), PlayerSessionId.New(), "Player", true, true, team, now, now);
        var attacker = Player(76561198000000001, PlayerTeam.Terrorist);
        var victim = Player(76561198000000002, PlayerTeam.CounterTerrorist);
        var assister = Player(76561198000000003, PlayerTeam.Terrorist);
        RankDeathInput Input(string weapon = "ak47", IReadOnlyList<GameplayStatKind>? specials = null)
            => new(new(Guid.NewGuid(), now, false, 8, "round"), new(victim, victim.Team, false),
                new(attacker, attacker.Team, false), assister, weapon, specials ?? [], false, 0);
        long Award(RankDeathInput input, PlayerId id, int streak = 1)
            => policy.Death(input, 1000, 1000, [], streak).Single(value => value.PlayerId == id).Points;
        var basic = Input();
        Assert.AreEqual(2L, Award(basic, attacker.Id));
        Assert.AreEqual(-2L, Award(basic, victim.Id));
        Assert.AreEqual(1L, Award(basic, assister.Id));
        Assert.AreEqual(4L, Award(Input(specials: [GameplayStatKind.HeadshotKill, GameplayStatKind.NoScopeKill]), attacker.Id));
        Assert.AreEqual(5L, Award(Input("knife"), attacker.Id));
        Assert.AreEqual(7L, Award(Input("taser"), attacker.Id));
        Assert.AreEqual(12L, Award(Input("flashbang"), attacker.Id));
        Assert.AreEqual(3L, Award(basic, attacker.Id, 3));
        Assert.AreEqual(-5L, Award(basic with { Attacker = null }, victim.Id));
        var teamkill = basic with { Victim = new(victim, PlayerTeam.Terrorist, false), FlashAssist = true };
        Assert.AreEqual(-6L, Award(teamkill, attacker.Id));
        Assert.AreEqual(-5L, Award(teamkill, assister.Id));
        Assert.AreEqual(2L, policy.Gameplay(GameplayStatKind.RoundWon, false));
        Assert.AreEqual(-2L, policy.Gameplay(GameplayStatKind.RoundLost, false));
        Assert.AreEqual(0, policy.Death(basic with { Context = basic.Context with { HumanPlayers = 7 } }, 1000, 1000, [], 1).Count);
    }
}
