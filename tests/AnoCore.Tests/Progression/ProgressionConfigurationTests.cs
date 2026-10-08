using AnoCore.Modules.Progression;
using AnoCore.Runtime.Configuration;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class ProgressionConfigurationTests
{
    [TestMethod]
    public async Task MissingSharedFileCopiesLegacyPolicyOnceWithoutChangingLegacyFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ano-progression-" + Guid.NewGuid());
        try
        {
            var store = new JsonConfigStore(root);
            var at = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
            var legacy = new AchievementConfiguration
            {
                Levels = [new(1, 0), new(2, 1234)],
                Boosts = [new("legacy-weekend", at, at.AddDays(2), 3)],
            };
            await store.SaveAsync("achievements", legacy);
            var before = await File.ReadAllTextAsync(Path.Combine(root, "achievements.json"));
            var first = await ProgressionConfiguration.LoadAsync(store);
            Assert.AreEqual(1234L, first.Levels[1].MinimumXp);
            Assert.AreEqual(3m, first.Boosts.Single().Multiplier);
            Assert.AreEqual(before, await File.ReadAllTextAsync(Path.Combine(root, "achievements.json")));
            legacy.Levels = [new(1, 0), new(2, 9999)];
            await store.SaveAsync("achievements", legacy);
            var restart = await ProgressionConfiguration.LoadAsync(store);
            Assert.AreEqual(1234L, restart.Levels[1].MinimumXp);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ExistingSharedFileDoesNotDependOnLegacyJsonAndInvalidSharedPolicyDoesNotFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "ano-progression-" + Guid.NewGuid());
        try
        {
            var store = new JsonConfigStore(root);
            await store.SaveAsync("progression", new ProgressionConfiguration { Levels = [new(1, 0), new(2, 500)] });
            await File.WriteAllTextAsync(Path.Combine(root, "achievements.json"), "broken legacy JSON");
            Assert.AreEqual(500L, (await ProgressionConfiguration.LoadAsync(store)).Levels[1].MinimumXp);
            await File.WriteAllTextAsync(Path.Combine(root, "progression.json"), "{\"Levels\":[]}");
            await Assert.ThrowsAsync<Exception>(async () => await ProgressionConfiguration.LoadAsync(store));
            Assert.AreEqual("{\"Levels\":[]}", await File.ReadAllTextAsync(Path.Combine(root, "progression.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void BoostNamesAreValidatedAndSnapshotsPreserveNamesAndOverlapSelection()
    {
        var at = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        var config = new ProgressionConfiguration
        {
            Boosts = [new("event", at, at.AddDays(1), 3) { Name = "Halloween XP" }],
        };
        var snapshot = config.Snapshot();
        config.Boosts.Clear();
        Assert.AreEqual("Halloween XP", snapshot.Boosts.Single().Name);
        Assert.AreEqual(3m, snapshot.ResolveBoost(at, ProgressionXpSource.Gameplay).Multiplier);
        Assert.IsNotEmpty(ProgressionConfiguration.Validate(new()
        { Boosts = [new("bad", at, at.AddDays(1), 2) { Name = "bad\nname" }] }));
    }
}
