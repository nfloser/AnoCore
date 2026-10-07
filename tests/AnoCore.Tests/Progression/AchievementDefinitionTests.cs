using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Progression;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class AchievementDefinitionTests
{
    [TestMethod]
    public void Prerequisites_AreBoundedValidatedAndSnapshotted()
    {
        var prerequisites = new List<AchievementPrerequisite> { new("first", 2) };
        var definition = AchievementDefinition.Create("second", 1, GameplayStatKind.Mvp,
            [new(1, 10, 100)], prerequisites);
        prerequisites.Clear();
        Assert.AreEqual(new AchievementPrerequisite("first", 2), definition.Prerequisites.Single());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<AchievementPrerequisite>)definition.Prerequisites).Clear());
        foreach (var invalid in new AchievementPrerequisite[][]
        {
            [new("second", 1)], [new("bad id", 1)], [new("first", 0)],
            [new("first", 101)], [null!], [new("first", 1), new("first", 2)],
        })
            Assert.ThrowsExactly<ArgumentException>(() => AchievementDefinition.Create("second", 1,
                GameplayStatKind.Mvp, [new(1, 10, 100)], invalid));
        Assert.ThrowsExactly<ArgumentException>(() => AchievementDefinition.Create("second", 1,
            GameplayStatKind.Mvp, [new(1, 10, 100)], Enumerable.Range(0, 33)
                .Select(index => new AchievementPrerequisite("a" + index, 1))));
        Assert.IsEmpty(Definition().Prerequisites);
    }

    [TestMethod]
    public void Catalog_RejectsUnknownTiersAndCyclesAndOrdersDependenciesBeforeChildren()
    {
        static AchievementCatalogEntry Entry(string id, params AchievementPrerequisite[] prerequisites)
            => new(id, 1, id, GameplayStatKind.Mvp, [new(1, 1, 10)])
            { Prerequisites = prerequisites.ToList() };
        var configuration = new AchievementConfiguration
        {
            Achievements = [Entry("a-child", new("z-parent", 1)), Entry("z-parent")],
        };
        CollectionAssert.AreEqual(new[] { "z-parent", "a-child" }, configuration.Snapshot()
            .Achievements.Select(entry => entry.Definition.Id).ToArray());
        foreach (var invalid in new List<AchievementCatalogEntry>[]
        {
            [Entry("a", new("missing", 1))],
            [Entry("a", new("b", 2)), Entry("b")],
            [Entry("a", new("b", 1)), Entry("b", new("a", 1))],
            [Entry("a", new("B", 1)), Entry("b")],
            [Entry("a") with { Prerequisites = null! }],
        })
        {
            configuration.Achievements = invalid;
            Assert.IsNotEmpty(AchievementConfiguration.Validate(configuration));
        }
    }

    private static AchievementDefinition Definition() => AchievementDefinition.Create(
        "headshots", 1, GameplayStatKind.HeadshotKill,
        [new(1, 10, 100), new(2, 50, 200), new(3, 100, 300)]);

    [TestMethod]
    public void Evaluation_ReportsIncompleteProgressAndExactTierBoundary()
    {
        var definition = Definition();
        var empty = definition.Evaluate([], 0);
        Assert.AreEqual(0L, empty.Count);
        Assert.AreEqual(0, empty.UnlockedTier);
        Assert.AreEqual(10L, empty.NextTarget);
        Assert.IsEmpty(empty.NewlyUnlocked);
        var boundary = definition.Evaluate([new(GameplayStatKind.HeadshotKill, 10)], 0);
        Assert.AreEqual(1, boundary.UnlockedTier);
        Assert.AreEqual(50L, boundary.NextTarget);
        Assert.AreEqual(100L, boundary.NewlyUnlocked.Single().RewardXp);
    }

    [TestMethod]
    public void Evaluation_ReportsEveryCrossedTierAndSuppressesAlreadyAwardedTiers()
    {
        var definition = Definition();
        var jump = definition.Evaluate([new(GameplayStatKind.HeadshotKill, long.MaxValue)], 1);
        CollectionAssert.AreEqual(new[] { 2, 3 }, jump.NewlyUnlocked.Select(tier => tier.Tier).ToArray());
        Assert.AreEqual(3, jump.UnlockedTier);
        Assert.IsNull(jump.NextTarget);
        Assert.IsEmpty(definition.Evaluate([new(GameplayStatKind.HeadshotKill, long.MaxValue)], 3).NewlyUnlocked);
    }

    [TestMethod]
    public void Evaluation_PreservesPermanentUnlocksAfterStatisticsReset()
    {
        var result = Definition().Evaluate([], 2);
        Assert.AreEqual(2, result.UnlockedTier);
        Assert.AreEqual(100L, result.NextTarget);
        Assert.IsEmpty(result.NewlyUnlocked);
    }

    [TestMethod]
    public void Definition_SnapshotsInputAndIgnoresUnrelatedValidStatistics()
    {
        var tiers = new List<AchievementTier> { new(1, 10, 100) };
        var definition = AchievementDefinition.Create("plants", 2, GameplayStatKind.BombPlanted, tiers);
        tiers[0] = new(1, 1, 999);
        var result = definition.Evaluate([new(GameplayStatKind.BombPlanted, 5), new(GameplayStatKind.Mvp, 100)], 0);
        Assert.IsEmpty(result.NewlyUnlocked);
        Assert.AreEqual(10L, result.NextTarget);
        Assert.AreEqual(2, definition.Version);
        Assert.AreEqual(100L, definition.Tiers[0].RewardXp);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<AchievementTier>)definition.Tiers).Clear());
    }

    [TestMethod]
    public void Definition_RejectsMalformedAndUnboundedDefinitions()
    {
        foreach (var tiers in new AchievementTier[][]
        {
            [], [new(2, 10, 0)], [new(1, 0, 0)], [new(1, 10, -1)],
            [new(1, 10, 0), new(2, 10, 0)], [null!],
        })
            Assert.ThrowsExactly<ArgumentException>(() => AchievementDefinition.Create("valid", 1, GameplayStatKind.Mvp, tiers));
        foreach (var id in new[] { "", " leading", "bad\n", "with space", new string('a', 65) })
            Assert.ThrowsExactly<ArgumentException>(() => AchievementDefinition.Create(id, 1, GameplayStatKind.Mvp, [new(1, 1, 0)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AchievementDefinition.Create("valid", 0, GameplayStatKind.Mvp, [new(1, 1, 0)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AchievementDefinition.Create("valid", 1, (GameplayStatKind)255, [new(1, 1, 0)]));
        Assert.ThrowsExactly<ArgumentException>(() => AchievementDefinition.Create("valid", 1, GameplayStatKind.Mvp,
            Enumerable.Range(1, 101).Select(index => new AchievementTier(index, index, 0))));
    }

    [TestMethod]
    public void Definition_StopsReadingAfterTheBoundedRejectionSentinel()
    {
        static IEnumerable<AchievementTier> TooManyTiers()
        {
            for (var index = 1; index <= AchievementDefinition.MaxTiers + 1; index++)
                yield return new AchievementTier(index, index, 0);
            throw new InvalidOperationException("Validation must stop before reading this element.");
        }

        Assert.ThrowsExactly<ArgumentException>(() => AchievementDefinition.Create(
            "bounded", 1, GameplayStatKind.Mvp, TooManyTiers()));
    }

    [TestMethod]
    public void Evaluation_AcceptsMaximumTargetsWithoutOverflow()
    {
        var definition = AchievementDefinition.Create("maximum", int.MaxValue,
            GameplayStatKind.Mvp, [new(1, long.MaxValue, long.MaxValue)]);
        var incomplete = definition.Evaluate([new(GameplayStatKind.Mvp, long.MaxValue - 1)], 0);
        Assert.IsEmpty(incomplete.NewlyUnlocked);
        Assert.AreEqual(long.MaxValue, incomplete.NextTarget);
        var complete = definition.Evaluate([new(GameplayStatKind.Mvp, long.MaxValue)], 0);
        Assert.AreEqual(long.MaxValue, complete.NewlyUnlocked.Single().RewardXp);
        Assert.IsNull(complete.NextTarget);
    }

    [TestMethod]
    public void Evaluation_RejectsMalformedAggregatesAndInvalidAwardedState()
    {
        var definition = Definition();
        foreach (var totals in new GameplayStatTotal[][]
        {
            [new(GameplayStatKind.Mvp, -1)], [new((GameplayStatKind)255, 1)], [null!],
            [new(GameplayStatKind.HeadshotKill, 5), new(GameplayStatKind.HeadshotKill, 5)],
        })
            Assert.ThrowsExactly<ArgumentException>(() => definition.Evaluate(totals, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => definition.Evaluate([], -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => definition.Evaluate([], 4));
    }
}
