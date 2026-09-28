using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Settings;

[TestClass]
public sealed class PlayerSettingsServiceTests
{
    private static readonly PlayerId Player = new(76561198000000993);

    [TestMethod]
    public async Task GetAsync_ReturnsDefaultWhenMissing()
    {
        var service = new PlayerSettingsService(new MemoryStore());
        var key = new PlayerSettingKey<int>("ui.page_size", 8);

        Assert.AreEqual(8, await service.GetAsync(Player, key));
    }

    [TestMethod]
    public async Task SetAsync_RoundTripsTypedValueAcrossServiceInstances()
    {
        var store = new MemoryStore();
        var key = new PlayerSettingKey<bool>("chat.compact", false);
        var first = new PlayerSettingsService(store);
        await first.SetAsync(Player, key, true);

        var second = new PlayerSettingsService(store);
        Assert.IsTrue(await second.GetAsync(Player, key));
    }

    [TestMethod]
    public async Task SettingsChanged_FollowsCommittedSetAndOnlyEffectiveResetWithoutValues()
    {
        var store = new MemoryStore();
        var events = new AnoEventBus();
        var observed = new List<PlayerSettingChangedEvent>();
        using var subscription = events.Subscribe<PlayerSettingChangedEvent>(
            (value, _) =>
            {
                observed.Add(value);
                return ValueTask.CompletedTask;
            });
        var service = new PlayerSettingsService(store, events: events);
        var key = new PlayerSettingKey<string>("chat.preference", "default");

        Assert.IsFalse(await service.ResetAsync(Player, key));
        await service.SetAsync(Player, key, "private-value");
        Assert.AreEqual("private-value", await service.GetAsync(Player, key));
        Assert.IsTrue(await service.ResetAsync(Player, key));
        Assert.IsFalse(await service.ResetAsync(Player, key));

        Assert.AreEqual(2, observed.Count);
        Assert.AreEqual(Player, observed[0].Player);
        Assert.AreEqual(key.Name, observed[0].SettingName);
        Assert.AreEqual(PlayerSettingChangeKind.Set, observed[0].Kind);
        Assert.AreEqual(PlayerSettingChangeKind.Reset, observed[1].Kind);
        Assert.IsFalse(observed.Any(change => change.ToString()!.Contains(
            "private-value", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SettingsChanged_DoesNotPublishWhenStorageFails()
    {
        var store = new MemoryStore { FailWrites = true };
        var events = new AnoEventBus();
        var observed = 0;
        using var subscription = events.Subscribe<PlayerSettingChangedEvent>(
            (_, _) =>
            {
                observed++;
                return ValueTask.CompletedTask;
            });
        var service = new PlayerSettingsService(store, events: events);
        var key = new PlayerSettingKey<int>("ui.scale", 1);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.SetAsync(Player, key, 2));
        Assert.AreEqual(0, observed);
    }

    [TestMethod]
    public async Task SettingsChanged_CancelledOrFailedDeleteNeverPublishes()
    {
        var store = new MemoryStore();
        var events = new AnoEventBus();
        var published = 0;
        using var subscription = events.Subscribe<PlayerSettingChangedEvent>(
            (_, _) =>
            {
                published++;
                return ValueTask.CompletedTask;
            });
        var service = new PlayerSettingsService(store, events: events);
        var key = new PlayerSettingKey<int>("ui.scale", 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await service.SetAsync(Player, key, 2, cancellation.Token));
        Assert.AreEqual(1, await service.GetAsync(Player, key));
        Assert.AreEqual(0, published);

        await service.SetAsync(Player, key, 2);
        store.FailDeletes = true;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.ResetAsync(Player, key));
        Assert.AreEqual(2, await service.GetAsync(Player, key));
        Assert.AreEqual(1, published);
    }

    [TestMethod]
    public async Task SettingsChanged_SubscriberFailureCannotFailCommittedMutation()
    {
        var store = new MemoryStore();
        var events = new AnoEventBus();
        var reported = new List<Exception>();
        using var subscription = events.Subscribe<PlayerSettingChangedEvent>(
            (_, _) => throw new InvalidOperationException("observer failed"));
        var service = new PlayerSettingsService(
            store, events: events, onEventFailure: reported.Add);
        var key = new PlayerSettingKey<int>("ui.scale", 1);

        await service.SetAsync(Player, key, 2);
        Assert.AreEqual(2, await service.GetAsync(Player, key));
        Assert.IsTrue(await service.ResetAsync(Player, key));
        Assert.AreEqual(1, await service.GetAsync(Player, key));
        Assert.AreEqual(2, reported.Count);
    }

    [TestMethod]
    public void SettingKey_RejectsUnsafeNames()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PlayerSettingKey<string>("../secret", "x"));
        Assert.ThrowsExactly<ArgumentException>(() => new PlayerSettingKey<string>("Upper Case", "x"));
    }

    private sealed class MemoryStore : IModuleDataStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public bool FailWrites { get; set; }
        public bool FailDeletes { get; set; }

        public ValueTask<string?> GetAsync(ModuleId module, string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_values.GetValueOrDefault($"{module.Value}:{key}"));

        public ValueTask SetAsync(ModuleId module, string key, string json, CancellationToken cancellationToken = default)
        {
            if (FailWrites)
                throw new InvalidOperationException("Storage unavailable.");
            _values[$"{module.Value}:{key}"] = json;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(ModuleId module, string key, CancellationToken cancellationToken = default)
        {
            if (FailDeletes)
                throw new InvalidOperationException("Storage unavailable.");
            return ValueTask.FromResult(_values.Remove($"{module.Value}:{key}"));
        }
    }
}
