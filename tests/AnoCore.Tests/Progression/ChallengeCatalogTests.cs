using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Progression;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class ChallengeCatalogTests
{
    private static readonly DateTimeOffset Monday =
        new(2027, 1, 4, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Evaluation_UsesHalfOpenUtcWindowAndExactTarget()
    {
        var catalog = ChallengeCatalogSnapshot.Create(
            [Daily("daily.headshots", GameplayStatKind.HeadshotKill, 10)]);

        var before = catalog.Evaluate(
            "daily.headshots",
            [new(GameplayStatKind.HeadshotKill, 100)],
            [],
            Monday.AddTicks(-1));
        var ready = catalog.Evaluate(
            "daily.headshots",
            [new(GameplayStatKind.HeadshotKill, 10)],
            [],
            Monday);
        var expired = catalog.Evaluate(
            "daily.headshots",
            [new(GameplayStatKind.HeadshotKill, 10)],
            [],
            Monday.AddDays(1));

        Assert.AreEqual(ChallengeEvaluationState.Future, before.State);
        Assert.AreEqual(ChallengeEvaluationState.ReadyToComplete, ready.State);
        Assert.IsTrue(ready.CompletionCandidate);
        Assert.AreEqual(10L, ready.Progress);
        Assert.AreEqual(ChallengeEvaluationState.Expired, expired.State);
        Assert.IsFalse(expired.CompletionCandidate);
    }

    [TestMethod]
    public void CompletedChallenge_RemainsCompletedAfterExpiry()
    {
        var catalog = ChallengeCatalogSnapshot.Create(
            [Daily("daily.mvp", GameplayStatKind.Mvp, 3)]);

        var result = catalog.Evaluate(
            "daily.mvp",
            [],
            ["daily.mvp"],
            Monday.AddDays(3));

        Assert.AreEqual(ChallengeEvaluationState.Completed, result.State);
        Assert.IsFalse(result.CompletionCandidate);
    }

    [TestMethod]
    public void Prerequisite_GatesCompletionUntilCommitted()
    {
        var catalog = ChallengeCatalogSnapshot.Create(
        [
            Daily("daily.first", GameplayStatKind.HeadshotKill, 1),
            Daily(
                "daily.second",
                GameplayStatKind.Mvp,
                1,
                ["daily.first"]),
        ]);

        var locked = catalog.Evaluate(
            "daily.second",
            [new(GameplayStatKind.Mvp, 5)],
            [],
            Monday.AddHours(1));
        var ready = catalog.Evaluate(
            "daily.second",
            [new(GameplayStatKind.Mvp, 5)],
            ["daily.first"],
            Monday.AddHours(1));

        Assert.AreEqual(ChallengeEvaluationState.Locked, locked.State);
        CollectionAssert.AreEqual(
            new[] { "daily.first" },
            locked.MissingPrerequisites.ToArray());
        Assert.AreEqual(ChallengeEvaluationState.ReadyToComplete, ready.State);
        Assert.IsTrue(ready.CompletionCandidate);
    }

    [TestMethod]
    public void EvaluateAll_IsDeterministicAndDoesNotAutoCommitDependencies()
    {
        var catalog = ChallengeCatalogSnapshot.Create(
        [
            Daily("b", GameplayStatKind.Mvp, 1, ["a"]),
            Daily("a", GameplayStatKind.HeadshotKill, 1),
        ]);

        var results = catalog.EvaluateAll(
            [
                new(GameplayStatKind.Mvp, 1),
                new(GameplayStatKind.HeadshotKill, 1),
            ],
            [],
            Monday.AddHours(1));

        CollectionAssert.AreEqual(
            new[] { "a", "b" },
            results.Select(value => value.Definition.Id).ToArray());
        Assert.AreEqual(
            ChallengeEvaluationState.ReadyToComplete,
            results[0].State);
        Assert.AreEqual(
            ChallengeEvaluationState.Locked,
            results[1].State);
    }

    [TestMethod]
    public void Catalog_RejectsMissingAndCyclicDependencies()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            ChallengeCatalogSnapshot.Create(
            [
                Daily(
                    "dependent",
                    GameplayStatKind.Mvp,
                    1,
                    ["missing"]),
            ]));

        Assert.ThrowsExactly<ArgumentException>(() =>
            ChallengeCatalogSnapshot.Create(
            [
                Daily("a", GameplayStatKind.Mvp, 1, ["b"]),
                Daily("b", GameplayStatKind.Mvp, 1, ["a"]),
            ]));
    }

    [TestMethod]
    public void Definitions_RequireValidKindsWindowsAndInputs()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            ChallengeCatalogSnapshot.Create(
            [
                Daily("bad window", GameplayStatKind.Mvp, 1),
            ]));

        Assert.ThrowsExactly<ArgumentException>(() =>
            ChallengeCatalogSnapshot.Create(
            [
                new ChallengeDefinition(
                    "daily",
                    1,
                    "Daily",
                    ChallengeWindowKind.Daily,
                    GameplayStatKind.Mvp,
                    1,
                    0,
                    Monday,
                    Monday.AddHours(23),
                    []),
            ]));

        Assert.ThrowsExactly<ArgumentException>(() =>
            ChallengeCatalogSnapshot.Create(
            [
                new ChallengeDefinition(
                    "weekly",
                    1,
                    "Weekly",
                    ChallengeWindowKind.Weekly,
                    GameplayStatKind.Mvp,
                    1,
                    0,
                    Monday,
                    Monday.AddDays(6),
                    []),
            ]));

        Assert.ThrowsExactly<ArgumentException>(() =>
            ChallengeCatalogSnapshot.Create(
            [
                Daily("valid", (GameplayStatKind)255, 1),
            ]));
    }

    [TestMethod]
    public void Catalog_SnapshotsPrerequisitesAndOrdersDefinitions()
    {
        var prerequisites = new List<string> { "base" };
        var dependent = Daily(
            "dependent",
            GameplayStatKind.Mvp,
            2,
            prerequisites);
        var catalog = ChallengeCatalogSnapshot.Create(
        [
            dependent,
            Daily("base", GameplayStatKind.HeadshotKill, 1),
        ]);

        prerequisites.Clear();

        CollectionAssert.AreEqual(
            new[] { "base", "dependent" },
            catalog.Challenges.Select(value => value.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { "base" },
            catalog.Get("dependent").PrerequisiteIds.ToArray());
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<string>)catalog.Get("dependent").PrerequisiteIds).Clear());
    }

    [TestMethod]
    public void Evaluation_RejectsMalformedTotalsAndCompletionState()
    {
        var catalog = ChallengeCatalogSnapshot.Create(
            [Daily("daily", GameplayStatKind.Mvp, 1)]);

        Assert.ThrowsExactly<ArgumentException>(() =>
            catalog.Evaluate(
                "daily",
                [
                    new(GameplayStatKind.Mvp, 1),
                    new(GameplayStatKind.Mvp, 2),
                ],
                [],
                Monday));

        Assert.ThrowsExactly<ArgumentException>(() =>
            catalog.Evaluate(
                "daily",
                [new(GameplayStatKind.Mvp, -1)],
                [],
                Monday));

        Assert.ThrowsExactly<ArgumentException>(() =>
            catalog.Evaluate(
                "daily",
                [],
                ["unknown"],
                Monday));
    }

    [TestMethod]
    public void SeasonChallenge_AllowsArbitraryNonEmptyUtcWindow()
    {
        var season = new ChallengeDefinition(
            "season.wins",
            4,
            "Win the season",
            ChallengeWindowKind.Season,
            GameplayStatKind.MatchWon,
            20,
            1000,
            Monday,
            Monday.AddDays(90),
            []);

        var catalog = ChallengeCatalogSnapshot.Create([season]);
        var result = catalog.Evaluate(
            season.Id,
            [new(GameplayStatKind.MatchWon, 19)],
            [],
            Monday.AddDays(45));

        Assert.AreEqual(ChallengeEvaluationState.Active, result.State);
        Assert.AreEqual(19L, result.Progress);
        Assert.AreEqual(1000L, result.Definition.RewardXp);
        Assert.AreEqual(4, result.Definition.Version);
    }

    private static ChallengeDefinition Daily(
        string id,
        GameplayStatKind statistic,
        long target,
        IReadOnlyList<string>? prerequisites = null)
        => new(
            id,
            1,
            id,
            ChallengeWindowKind.Daily,
            statistic,
            target,
            100,
            Monday,
            Monday.AddDays(1),
            prerequisites ?? []);
}
