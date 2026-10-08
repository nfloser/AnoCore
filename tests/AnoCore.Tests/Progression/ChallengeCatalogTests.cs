using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Progression;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class ChallengeCatalogTests
{
    private static readonly DateTimeOffset Monday =
        new(2027, 1, 4, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void NativePredicateSnapshotsRetainBooleanFalseTeamsAndDistanceBounds()
    {
        var teams = new List<AnoCore.Abstractions.Players.PlayerTeam> { AnoCore.Abstractions.Players.PlayerTeam.CounterTerrorist };
        var definition = Daily("native", GameplayStatKind.HeadshotKill, 10) with
        {
            CounterSource = ChallengeCounterSource.CombatKills,
            Predicates = new()
            {
                Maps = ["de_mirage"],
                Weapons = ["awp"],
                AttackerTeams = teams,
                Headshot = true,
                NoScope = true,
                ThroughSmoke = true,
                AttackerBlind = false,
                PenetrationMinimum = 1,
                DistanceMinimumMeters = 30,
                DistanceMaximumMeters = 50
            },
        };
        var snapshot = ChallengeCatalogSnapshot.Create([definition]).Get("native");
        teams.Clear();
        Assert.AreEqual(1, snapshot.Predicates.AttackerTeams.Count);
        Assert.AreEqual(false, snapshot.Predicates.AttackerBlind);
        Assert.AreEqual(30m, snapshot.Predicates.DistanceMinimumMeters);
        Assert.IsTrue(snapshot.Predicates.HasFilters);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<AnoCore.Abstractions.Players.PlayerTeam>)snapshot.Predicates.AttackerTeams).Clear());
        var restored = System.Text.Json.JsonSerializer.Deserialize<ChallengeDefinition>(System.Text.Json.JsonSerializer.Serialize(snapshot))!;
        Assert.AreEqual(snapshot.Predicates.NoScope, restored.Predicates.NoScope);
        Assert.AreEqual(snapshot.Predicates.DistanceMaximumMeters, restored.Predicates.DistanceMaximumMeters);
    }

    [TestMethod]
    public void PredicatesAreImmutableValidatedAndPreservedByRecurringJsonSnapshots()
    {
        var maps = new List<string> { "de_dust2" };
        var weapons = new List<string> { "ak47" };
        var hitgroups = new List<int> { 1 };
        var template = new RecurringChallengeTemplate("weekly.damage", 1, "Damage", ChallengeWindowKind.Weekly,
            GameplayStatKind.HeadshotKill, 100, 20, [])
        {
            CounterSource = ChallengeCounterSource.DamageHealth,
            Predicates = new() { Maps = maps, Weapons = weapons, Hitgroups = hitgroups },
        };
        var snapshot = new ChallengeConfiguration { Recurring = [template] }.Snapshot();
        maps.Clear(); weapons.Clear(); hitgroups.Clear();
        var occurrence = snapshot.ResolveAt(Monday).Challenges.Single();
        CollectionAssert.AreEqual(new[] { "de_dust2" }, occurrence.Predicates.Maps.ToArray());
        CollectionAssert.AreEqual(new[] { "ak47" }, occurrence.Predicates.Weapons.ToArray());
        CollectionAssert.AreEqual(new[] { 1 }, occurrence.Predicates.Hitgroups.ToArray());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)occurrence.Predicates.Maps).Clear());
        var restored = System.Text.Json.JsonSerializer.Deserialize<ChallengeDefinition>(
            System.Text.Json.JsonSerializer.Serialize(occurrence))!;
        CollectionAssert.AreEqual(occurrence.Predicates.Weapons.ToArray(), restored.Predicates.Weapons.ToArray());
        Assert.IsEmpty(Daily("legacy", GameplayStatKind.Mvp, 1).Predicates.Maps);
    }

    [TestMethod]
    public void PredicatesRejectUnsupportedEvidenceMalformedValuesAndUnboundedLists()
    {
        var definition = Daily("test", GameplayStatKind.Mvp, 1);
        void Reject(ChallengePredicates predicates, ChallengeCounterSource source = ChallengeCounterSource.GameplayStat)
            => Assert.ThrowsExactly<ArgumentException>(() => ChallengeCatalogSnapshot.Create(
                [definition with { CounterSource = source, Predicates = predicates }]));
        Reject(new() { NoScope = true });
        Reject(new() { Headshot = true }, ChallengeCounterSource.DamageHealth);
        Reject(new() { AttackerTeams = [AnoCore.Abstractions.Players.PlayerTeam.Unknown] }, ChallengeCounterSource.CombatKills);
        Reject(new() { DistanceMinimumMeters = 50, DistanceMaximumMeters = 10 }, ChallengeCounterSource.CombatKills);
        Reject(new() { PenetrationMinimum = 33 }, ChallengeCounterSource.CombatKills);
        Reject(new() { Weapons = ["ak47"] });
        Reject(new() { Hitgroups = [1] });
        Reject(new() { Maps = [" DE_DUST2 "] });
        Reject(new() { Maps = ["de_dust2", "de_dust2"] });
        Reject(new() { Maps = Enumerable.Range(0, 17).Select(i => $"map{i}").ToArray() });
        Reject(new() { Maps = null! });
        Reject(new() { Hitgroups = [256] }, ChallengeCounterSource.DamageHealth);
        Reject(new() { Hitgroups = [1, 1] }, ChallengeCounterSource.UtilityDamage);
        Reject(new() { Weapons = ["weapon_ak47"] }, ChallengeCounterSource.DamageHealth);
        Reject(new() { Weapons = ["ak47"] }, ChallengeCounterSource.UtilityDamage);
        Reject(null!);
        Reject(new() { Maps = ["de_dust2' OR 1=1"] });
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<ChallengePredicates>("{\"Team\":\"CT\"}"));
        var valid = ChallengeCatalogSnapshot.Create([definition with { Predicates = new() { Maps = ["de_dust2"] } }]);
        Assert.AreEqual("de_dust2", valid.Get("test").Predicates.Maps.Single());
    }

    [TestMethod]
    public void CounterSourcesAreValidatedAndPreservedAcrossRecurringAndJsonSnapshots()
    {
        var template = new RecurringChallengeTemplate("weekly.kills", 1, "Weekly kills", ChallengeWindowKind.Weekly,
            AnoCore.Abstractions.Stats.GameplayStatKind.HeadshotKill, 20, 100, [])
        { CounterSource = ChallengeCounterSource.CombatKills };
        var configuration = new ChallengeConfiguration { Recurring = [template] };
        var snapshot = configuration.Snapshot();
        configuration.Recurring.Clear();
        var occurrence = snapshot.ResolveAt(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero)).Challenges.Single();
        Assert.AreEqual(ChallengeCounterSource.CombatKills, occurrence.CounterSource);
        var json = System.Text.Json.JsonSerializer.Serialize(occurrence);
        Assert.AreEqual(occurrence.CounterSource,
            System.Text.Json.JsonSerializer.Deserialize<ChallengeDefinition>(json)!.CounterSource);
        Assert.IsNotEmpty(ChallengeConfiguration.Validate(new() { Recurring = [template with { CounterSource = (ChallengeCounterSource)255 }] }));
        Assert.ThrowsExactly<ArgumentException>(() => ChallengeCatalogSnapshot.Create([occurrence with { CounterSource = (ChallengeCounterSource)255 }]));
        Assert.AreEqual(ChallengeCounterSource.GameplayStat, new ChallengeConfiguration().Snapshot()
            .ResolveAt(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero)).Challenges[0].CounterSource);
    }

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
    public void CatalogOrdering_IsDeterministicAndReadyPrerequisiteIsNotAutoCommitted()
    {
        var catalog = ChallengeCatalogSnapshot.Create(
        [
            Daily("b", GameplayStatKind.Mvp, 1, ["a"]),
            Daily("a", GameplayStatKind.HeadshotKill, 1),
        ]);

        CollectionAssert.AreEqual(
            new[] { "a", "b" },
            catalog.Challenges.Select(value => value.Id).ToArray());

        var prerequisite = catalog.Evaluate(
            "a",
            [new(GameplayStatKind.HeadshotKill, 1)],
            [],
            Monday.AddHours(1));
        var dependent = catalog.Evaluate(
            "b",
            [new(GameplayStatKind.Mvp, 1)],
            [],
            Monday.AddHours(1));

        Assert.AreEqual(
            ChallengeEvaluationState.ReadyToComplete,
            prerequisite.State);
        Assert.AreEqual(
            ChallengeEvaluationState.Locked,
            dependent.State);
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
    public void Catalog_RejectsInputsBeyondConfiguredBounds()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            ChallengeCatalogSnapshot.Create(
                Enumerable.Range(1, ChallengeCatalogSnapshot.MaxChallenges + 1)
                    .Select(index => Daily(
                        $"daily.{index}",
                        GameplayStatKind.Mvp,
                        1))));

        var prerequisites = Enumerable
            .Range(1, ChallengeCatalogSnapshot.MaxPrerequisites + 1)
            .Select(index => $"p{index}")
            .ToArray();
        Assert.ThrowsExactly<ArgumentException>(() =>
            ChallengeCatalogSnapshot.Create(
            [
                Daily(
                    "too-many",
                    GameplayStatKind.Mvp,
                    1,
                    prerequisites),
            ]));
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
