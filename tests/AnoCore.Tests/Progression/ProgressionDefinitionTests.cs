using AnoCore.Modules.Progression;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class ProgressionDefinitionTests
{
    private static readonly DateTimeOffset Friday =
        new(2026, 10, 9, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Levels_MapExactBoundariesDeterministically()
    {
        var snapshot = ProgressionDefinitionSnapshot.Create(
        [
            new(1, 0),
            new(2, 100),
            new(3, 250),
        ]);

        Assert.AreEqual(1, snapshot.LevelFor(0).Level);
        Assert.AreEqual(1, snapshot.LevelFor(99).Level);
        Assert.AreEqual(2, snapshot.LevelFor(100).Level);
        Assert.AreEqual(2, snapshot.LevelFor(249).Level);
        Assert.AreEqual(3, snapshot.LevelFor(250).Level);
        Assert.AreEqual(3, snapshot.LevelFor(long.MaxValue).Level);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.LevelFor(-1));
    }

    [TestMethod]
    public void Snapshot_DoesNotObserveCallerCollectionChanges()
    {
        var levels = new List<XpLevelThreshold>
        {
            new(1, 0),
            new(2, 100),
        };
        var boosts = new List<XpBoostDefinition>
        {
            new("weekend", Friday, Friday.AddDays(2), 2m),
        };

        var snapshot = ProgressionDefinitionSnapshot.Create(levels, boosts);
        levels.Add(new(3, 250));
        boosts.Clear();

        Assert.AreEqual(2, snapshot.Levels.Count);
        Assert.AreEqual(1, snapshot.Boosts.Count);
        Assert.AreEqual("weekend", snapshot.Boosts[0].Id);
    }

    [TestMethod]
    public void Boosts_UseUtcHalfOpenWindowsAndHighestEligibleMultiplier()
    {
        var snapshot = ProgressionDefinitionSnapshot.Create(
            [new(1, 0)],
            [
                new("weekend", Friday, Friday.AddDays(2), 2m),
                new("event", Friday.AddHours(1), Friday.AddHours(3), 3m),
            ]);

        Assert.AreEqual(2m,
            snapshot.ResolveBoost(Friday, ProgressionXpSource.Gameplay).Multiplier);
        Assert.AreEqual(3m,
            snapshot.ResolveBoost(Friday.AddHours(1), ProgressionXpSource.Gameplay).Multiplier);
        Assert.AreEqual(2m,
            snapshot.ResolveBoost(Friday.AddHours(3), ProgressionXpSource.Gameplay).Multiplier);
        Assert.AreEqual(1m,
            snapshot.ResolveBoost(Friday.AddDays(2), ProgressionXpSource.Gameplay).Multiplier);
        Assert.AreEqual(2m,
            snapshot.ResolveBoost(Friday.ToOffset(TimeSpan.FromHours(2)),
                ProgressionXpSource.Gameplay).Multiplier);
    }

    [TestMethod]
    public void RewardXp_IsNotBoostedUnlessDefinitionOptsIn()
    {
        var snapshot = ProgressionDefinitionSnapshot.Create(
            [new(1, 0)],
            [
                new("gameplay", Friday, Friday.AddDays(2), 2m),
                new("challenge-special", Friday, Friday.AddHours(1), 3m,
                    ProgressionXpSourceMask.Gameplay
                    | ProgressionXpSourceMask.ChallengeReward),
            ]);

        Assert.AreEqual(3m,
            snapshot.ResolveBoost(Friday, ProgressionXpSource.Gameplay).Multiplier);
        Assert.AreEqual(3m,
            snapshot.ResolveBoost(Friday, ProgressionXpSource.ChallengeReward).Multiplier);
        Assert.AreEqual(1m,
            snapshot.ResolveBoost(Friday, ProgressionXpSource.AchievementReward).Multiplier);
        Assert.AreEqual(1m,
            snapshot.ResolveBoost(Friday, ProgressionXpSource.Administration).Multiplier);
    }

    [TestMethod]
    public void ApplyBoost_TruncatesFractionalXpAndChecksOverflow()
    {
        var snapshot = ProgressionDefinitionSnapshot.Create(
            [new(1, 0)],
            [new("fractional", Friday, Friday.AddHours(1), 1.5m)]);

        Assert.AreEqual(4L,
            snapshot.ApplyBoost(3, Friday, ProgressionXpSource.Gameplay));
        Assert.AreEqual(3L,
            snapshot.ApplyBoost(3, Friday, ProgressionXpSource.ChallengeReward));
        Assert.AreEqual(0L,
            snapshot.ApplyBoost(0, Friday, ProgressionXpSource.Gameplay));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            snapshot.ApplyBoost(-1, Friday, ProgressionXpSource.Gameplay));
        Assert.ThrowsExactly<OverflowException>(() =>
            snapshot.ApplyBoost(long.MaxValue, Friday, ProgressionXpSource.Gameplay));
    }

    [TestMethod]
    public void Definitions_RejectInvalidLevelsAndBoosts()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProgressionDefinitionSnapshot.Create([]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProgressionDefinitionSnapshot.Create([new(2, 0)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProgressionDefinitionSnapshot.Create([new(1, 1)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProgressionDefinitionSnapshot.Create([new(1, 0), new(3, 100)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProgressionDefinitionSnapshot.Create([new(1, 0), new(2, 0)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProgressionDefinitionSnapshot.Create(
                [new(1, 0)],
                [new("", Friday, Friday.AddHours(1), 2m)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProgressionDefinitionSnapshot.Create(
                [new(1, 0)],
                [new("bad-time", Friday.ToOffset(TimeSpan.FromHours(2)),
                    Friday.AddHours(1), 2m)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProgressionDefinitionSnapshot.Create(
                [new(1, 0)],
                [new("bad-window", Friday, Friday, 2m)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProgressionDefinitionSnapshot.Create(
                [new(1, 0)],
                [new("bad-multiplier", Friday, Friday.AddHours(1), 0.5m)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProgressionDefinitionSnapshot.Create(
                [new(1, 0)],
                [new("bad-source", Friday, Friday.AddHours(1), 2m,
                    (ProgressionXpSourceMask)128)]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProgressionDefinitionSnapshot.Create(
                [new(1, 0)],
                [
                    new("duplicate", Friday, Friday.AddHours(1), 2m),
                    new("duplicate", Friday.AddHours(2), Friday.AddHours(3), 2m),
                ]));
    }
}
