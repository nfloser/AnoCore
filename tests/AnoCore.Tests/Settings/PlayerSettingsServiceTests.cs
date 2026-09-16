using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
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
    public void SettingKey_RejectsUnsafeNames()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PlayerSettingKey<string>("../secret", "x"));
        Assert.ThrowsExactly<ArgumentException>(() => new PlayerSettingKey<string>("Upper Case", "x"));
    }

    private sealed class MemoryStore : IModuleDataStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public ValueTask<string?> GetAsync(ModuleId module, string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_values.GetValueOrDefault($"{module.Value}:{key}"));

        public ValueTask SetAsync(ModuleId module, string key, string json, CancellationToken cancellationToken = default)
        {
            _values[$"{module.Value}:{key}"] = json;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(ModuleId module, string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_values.Remove($"{module.Value}:{key}"));
    }
}
