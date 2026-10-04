using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class GameplayStatContractTests
{
    private static readonly PlayerId Player = new(76561198000184001);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 16, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Event_NormalizesTimestampAndMap()
    {
        var value = new GameplayStatEvent(
            Guid.NewGuid(), Player, Now.AddTicks(17), " de_dust2 ",
            GameplayStatKind.BombPlanted);

        Assert.AreEqual(Now.AddTicks(10), value.OccurredAtUtc);
        Assert.AreEqual("de_dust2", value.MapName);
        Assert.AreEqual(1, value.Amount);
    }

    [TestMethod]
    public void Event_RejectsInvalidIdentityKindMapAndAmount()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new GameplayStatEvent(Guid.Empty, Player, Now, "de_dust2",
                GameplayStatKind.BombPlanted));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new GameplayStatEvent(Guid.NewGuid(), Player, Now, " ",
                GameplayStatKind.BombPlanted));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new GameplayStatEvent(Guid.NewGuid(), Player, Now, "de_dust2",
                (GameplayStatKind)255));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new GameplayStatEvent(Guid.NewGuid(), Player, Now, "de_dust2",
                GameplayStatKind.BombPlanted, 0));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new GameplayStatFilter("de_dust2\nother"));
    }

    [TestMethod]
    public void Configuration_ValidatesWarmupMinimumPlayersAndFfa()
    {
        var configuration = GameplayStatsConfiguration.Default;
        Assert.IsFalse(configuration.WarmupStats);
        Assert.AreEqual(4, configuration.MinimumPlayers);
        Assert.IsFalse(configuration.FreeForAll);
        Assert.AreEqual(0, GameplayStatsConfiguration.Validate(configuration).Count);

        configuration.MinimumPlayers = 0;
        Assert.AreEqual(1, GameplayStatsConfiguration.Validate(configuration).Count);
        configuration.MinimumPlayers = 65;
        Assert.AreEqual(1, GameplayStatsConfiguration.Validate(configuration).Count);
    }

    [TestMethod]
    public void Eligibility_UsesWarmupAndTrackedHumanThreshold()
    {
        var configuration = GameplayStatsConfiguration.Default;

        Assert.IsFalse(GameplayStatsEligibility.IsAllowed(
            configuration, isWarmup: true, trackedHumanPlayers: 10));
        Assert.IsFalse(GameplayStatsEligibility.IsAllowed(
            configuration, isWarmup: false, trackedHumanPlayers: 3));
        Assert.IsTrue(GameplayStatsEligibility.IsAllowed(
            configuration, isWarmup: false, trackedHumanPlayers: 4));

        configuration.WarmupStats = true;
        Assert.IsTrue(GameplayStatsEligibility.IsAllowed(
            configuration, isWarmup: true, trackedHumanPlayers: 4));
    }
}
