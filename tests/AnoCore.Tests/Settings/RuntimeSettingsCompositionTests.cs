using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Runtime.Composition;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Settings;

[TestClass]
[DoNotParallelize]
public sealed class RuntimeSettingsCompositionTests
{
    [TestMethod]
    public async Task RuntimeSettings_PublishesCommittedChangesThroughSharedEventBus()
    {
        var connectionString = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");

        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var configPath = Path.Combine(Path.GetTempPath(),
            "ano-settings-" + Guid.NewGuid().ToString("N"));
        using var runtime = await RuntimeServices.CreateAsync(
            new MySqlDatabase(connectionString!),
            new JsonConfigStore(configPath),
            events,
            players);
        Assert.AreSame(runtime.Settings, runtime.GetService(typeof(IPlayerSettingsService)));
        var changes = new List<PlayerSettingChangedEvent>();
        using var subscription = events.Subscribe<PlayerSettingChangedEvent>(
            (value, _) =>
            {
                changes.Add(value);
                return ValueTask.CompletedTask;
            });
        var player = new PlayerId(76561198000012801);
        var key = new PlayerSettingKey<string>(
            "test.event." + Guid.NewGuid().ToString("N"), "");
        await runtime.Settings.SetAsync(player, key, "value");
        Assert.AreEqual("value", await runtime.Settings.GetAsync(player, key));
        Assert.IsTrue(await runtime.Settings.ResetAsync(player, key));

        Assert.AreEqual(2, changes.Count);
        Assert.AreEqual(player, changes[0].Player);
        Assert.AreEqual(key.Name, changes[0].SettingName);
        Assert.AreEqual(PlayerSettingChangeKind.Set, changes[0].Kind);
        Assert.AreEqual(PlayerSettingChangeKind.Reset, changes[1].Kind);
    }
}
