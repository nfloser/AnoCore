using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class GameplayStatEventFactoryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 18, 0, 0, TimeSpan.Zero);
    private static readonly PlayerSnapshot Terrorist = Snapshot(
        76561198000184101, PlayerTeam.Terrorist);
    private static readonly PlayerSnapshot CounterTerrorist = Snapshot(
        76561198000184102, PlayerTeam.CounterTerrorist);

    [TestMethod]
    public void WeaponKillFactsUseExistingFamilyRulesAndStableVictimIdentity()
    {
        foreach (var (weapon, expected) in new[] { ("knife_karambit", GameplayStatKind.KnifeKill),
            ("hegrenade", GameplayStatKind.GrenadeKill), ("inferno", GameplayStatKind.InfernoKill),
            ("flashbang", GameplayStatKind.ImpactKill), ("taser", GameplayStatKind.TaserKill) })
        {
            var first = GameplayStatEventFactory.WeaponKill("server", "de_test", 1, 100, Now,
                Terrorist.Id, CounterTerrorist.Id, weapon);
            Assert.AreEqual(expected, first!.Kind);
            var replay = GameplayStatEventFactory.WeaponKill("server", "de_test", 1, 100, Now.AddSeconds(1),
                Terrorist.Id, CounterTerrorist.Id, weapon);
            Assert.AreEqual(first.EventId, replay!.EventId);
        }
        Assert.IsNull(GameplayStatEventFactory.WeaponKill("server", "de_test", 1, 100, Now,
            Terrorist.Id, CounterTerrorist.Id, "ak47"));
        Assert.IsNull(GameplayStatEventFactory.WeaponKill("server", "de_test", 1, 100, Now,
            Terrorist.Id, Terrorist.Id, "knife"));
    }

    [TestMethod]
    public void TeamObjectiveFactsExcludeOtherTeamDisconnectedAndActorWithReplayStableIds()
    {
        var disconnected = new PlayerSnapshot(new(76561198000184103), PlayerSessionId.New(), "Disconnected", false, true, PlayerTeam.CounterTerrorist, Now, Now);
        var other = Snapshot(76561198000184104, PlayerTeam.CounterTerrorist);
        var facts = GameplayStatEventFactory.TeamObjective("server", "de_test", 1, 100, Now,
            [Terrorist, CounterTerrorist, disconnected, other], GameplayStatKind.BombDefusedOthers,
            PlayerTeam.CounterTerrorist, CounterTerrorist.Id);
        Assert.HasCount(1, facts);
        Assert.AreEqual(other.Id, facts.Single().PlayerId);
        var retry = GameplayStatEventFactory.TeamObjective("server", "de_test", 1, 100, Now.AddSeconds(1),
            [other], GameplayStatKind.BombDefusedOthers, PlayerTeam.CounterTerrorist, CounterTerrorist.Id);
        Assert.AreEqual(facts.Single().EventId, retry.Single().EventId);
    }

    [TestMethod]
    public void RoundEvents_RecordParticipationTeamAndOutcomeDeterministically()
    {
        var first = GameplayStatEventFactory.Round(
            "server", "de_dust2", 10, 100, Now,
            [Terrorist, CounterTerrorist], PlayerTeam.CounterTerrorist);
        var replay = GameplayStatEventFactory.Round(
            "server", "de_dust2", 10, 100, Now,
            [Terrorist, CounterTerrorist], PlayerTeam.CounterTerrorist);

        Assert.AreEqual(6, first.Count);
        CollectionAssert.AreEqual(
            first.Select(x => x.EventId).ToArray(),
            replay.Select(x => x.EventId).ToArray());
        CollectionAssert.Contains(
            first.Where(x => x.PlayerId == Terrorist.Id).Select(x => x.Kind).ToArray(),
            GameplayStatKind.RoundLost);
        CollectionAssert.Contains(
            first.Where(x => x.PlayerId == CounterTerrorist.Id).Select(x => x.Kind).ToArray(),
            GameplayStatKind.RoundWon);
        CollectionAssert.Contains(
            first.Where(x => x.PlayerId == Terrorist.Id).Select(x => x.Kind).ToArray(),
            GameplayStatKind.RoundTerrorist);
    }

    [TestMethod]
    public void TeamMatch_RecordsWinnerAndLoser()
    {
        var participants = new[]
        {
            new GameplayMatchParticipant(Terrorist.Id, Terrorist.Team, 12),
            new GameplayMatchParticipant(CounterTerrorist.Id, CounterTerrorist.Team, 8),
        };

        var events = GameplayStatEventFactory.Match(
            "server", "de_dust2", 10, 200, Now, participants,
            freeForAll: false, PlayerTeam.Terrorist);

        Assert.AreEqual(GameplayStatKind.MatchWon,
            events.Single(x => x.PlayerId == Terrorist.Id).Kind);
        Assert.AreEqual(GameplayStatKind.MatchLost,
            events.Single(x => x.PlayerId == CounterTerrorist.Id).Kind);
    }

    [TestMethod]
    public void FfaMatch_UsesHighestScoreAndStableSteamIdTieBreak()
    {
        var lower = new PlayerId(76561198000184110);
        var higher = new PlayerId(76561198000184111);
        var events = GameplayStatEventFactory.Match(
            "server", "de_dust2", 10, 201, Now,
            [
                new GameplayMatchParticipant(higher, PlayerTeam.Terrorist, 20),
                new GameplayMatchParticipant(lower, PlayerTeam.CounterTerrorist, 20),
            ],
            freeForAll: true, PlayerTeam.Unknown);

        Assert.AreEqual(GameplayStatKind.MatchWon,
            events.Single(x => x.PlayerId == lower).Kind);
        Assert.AreEqual(GameplayStatKind.MatchLost,
            events.Single(x => x.PlayerId == higher).Kind);
    }

    private static PlayerSnapshot Snapshot(ulong steamId, PlayerTeam team)
        => new(new PlayerId(steamId), PlayerSessionId.New(), "Player", true, true,
            team, Now, Now);
}
