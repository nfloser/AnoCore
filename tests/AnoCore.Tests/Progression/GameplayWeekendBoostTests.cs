using AnoCore.Modules.Progression;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class GameplayWeekendBoostTests
{
    private static readonly DateTimeOffset Saturday = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private static ProgressionDefinitionSnapshot Definitions(params XpBoostDefinition[] boosts)
        => ProgressionDefinitionSnapshot.Create([new(1, 0)], boosts);

    [TestMethod]
    public void RecurringWeekend_UsesExactUtcBoundariesAndStableOccurrenceId()
    {
        var policy = new GameplayXpConfiguration { WeekendMultiplier = 2 }.Snapshot();
        Assert.AreEqual(1m, policy.ResolveGameplayBoost(Definitions(), Saturday.AddTicks(-10)).Multiplier);
        var atStart = policy.ResolveGameplayBoost(Definitions(), Saturday);
        var sunday = policy.ResolveGameplayBoost(Definitions(), Saturday.AddDays(1).ToOffset(TimeSpan.FromHours(2)));
        Assert.AreEqual(2m, atStart.Multiplier);
        Assert.AreEqual("gameplay.weekend.20261010", atStart.BoostId);
        Assert.AreEqual(atStart, sunday);
        Assert.AreEqual(1m, policy.ResolveGameplayBoost(Definitions(), Saturday.AddDays(2)).Multiplier);
        Assert.AreEqual("gameplay.weekend.20261017",
            policy.ResolveGameplayBoost(Definitions(), Saturday.AddDays(7)).BoostId);
    }

    [TestMethod]
    public void Overlap_UsesStrongestBoostAndOrdinalTieWithoutMultiplying()
    {
        var policy = new GameplayXpConfiguration { WeekendMultiplier = 2 }.Snapshot();
        var stronger = Definitions(new XpBoostDefinition("event", Saturday, Saturday.AddDays(2), 3));
        Assert.AreEqual(new XpBoostResolution("event", 3), policy.ResolveGameplayBoost(stronger, Saturday));
        var tie = Definitions(new XpBoostDefinition("aaa", Saturday, Saturday.AddDays(2), 2));
        Assert.AreEqual(new XpBoostResolution("aaa", 2), policy.ResolveGameplayBoost(tie, Saturday));
        var rewardOnly = Definitions(new XpBoostDefinition("rewards", Saturday, Saturday.AddDays(2), 5, ProgressionXpSourceMask.ChallengeReward));
        Assert.AreEqual(2m, policy.ResolveGameplayBoost(rewardOnly, Saturday).Multiplier);
        Assert.AreEqual(1m, Definitions().ResolveBoost(Saturday, ProgressionXpSource.AchievementReward).Multiplier);
    }

    [TestMethod]
    public void Configuration_DefaultsOffBoundsMultiplierAndSnapshotsChanges()
    {
        var configuration = new GameplayXpConfiguration();
        Assert.AreEqual(1m, configuration.Snapshot().ResolveGameplayBoost(Definitions(), Saturday).Multiplier);
        configuration.WeekendMultiplier = 2;
        var snapshot = configuration.Snapshot();
        configuration.WeekendMultiplier = 4;
        Assert.AreEqual(2m, snapshot.ResolveGameplayBoost(Definitions(), Saturday).Multiplier);
        Assert.IsNotEmpty(GameplayXpConfiguration.Validate(new() { WeekendMultiplier = 0.5m }));
        Assert.IsNotEmpty(GameplayXpConfiguration.Validate(new() { WeekendMultiplier = 11m }));
    }
}
