using AnoCore.Modules.AnoVeto;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.AnoVeto;

[TestClass]
[DoNotParallelize]
public sealed class AnoVetoPresetMariaDbTests
{
    private static MySqlDatabase Database()
    {
        var connectionString = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured.");
        return new MySqlDatabase(connectionString!);
    }

    private static AnoVetoPreset Preset(ulong owner, string name)
        => new(owner, name, Enumerable.Range(1, 8).Select(i => $"de_map{i}"));

    [TestMethod]
    public async Task Presets_SurviveRepositoryRecreationAndEnforceOwnerRevision()
    {
        var database = Database();
        await AnoVetoPresetPersistenceBootstrap.EnsureReadyAsync(database);
        var owner = 76561198000000000UL + (ulong)Random.Shared.Next(100000, 999999);
        var other = owner + 1000000;
        var first = new MySqlAnoVetoPresetRepository(database);
        var second = new MySqlAnoVetoPresetRepository(database);
        try
        {
            Assert.AreEqual(1L, await first.CreateAsync(Preset(owner, "Weekend")));
            Assert.IsNull(await second.GetAsync(other, "Weekend"));
            var loaded = await second.GetAsync(owner, "weekend");
            Assert.IsNotNull(loaded);
            Assert.AreEqual(1L, loaded.Value.Revision);
            Assert.AreEqual("de_map1", loaded.Value.Preset.MapIds[0]);
            var updated = loaded.Value.Preset.Rename("Friday");
            Assert.AreEqual(2L, await second.ReplaceAsync(owner, "Weekend", 1, updated));
            Assert.IsNull(await first.GetAsync(owner, "Weekend"));
            Assert.IsNotNull(await first.GetAsync(owner, "Friday"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
                await first.ReplaceAsync(owner, "Friday", 1, updated));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
                await first.DeleteAsync(other, "Friday", 2));
            await second.DeleteAsync(owner, "Friday", 2);
            Assert.HasCount(0, await first.ListAsync(owner));
        }
        finally
        {
            // Owners are uniquely generated so test failures cannot modify unrelated data.
            var current = await first.ListAsync(owner);
            foreach (var (preset, revision) in current)
                await first.DeleteAsync(owner, preset.Name, revision);
        }
    }

    [TestMethod]
    public async Task PresetQuota_IsEnforcedOnSharedDatabase()
    {
        var database = Database();
        await AnoVetoPresetPersistenceBootstrap.EnsureReadyAsync(database);
        var owner = 76561198002000000UL + (ulong)Random.Shared.Next(100000, 999999);
        var store = new MySqlAnoVetoPresetRepository(database);
        try
        {
            for (var i = 0; i < AnoVetoPresetCatalog.MaxPresetsPerOwner; i++)
                await store.CreateAsync(Preset(owner, $"Test {i:00}"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
                await store.CreateAsync(Preset(owner, "Overflow")));
        }
        finally
        {
            foreach (var (preset, revision) in await store.ListAsync(owner))
                await store.DeleteAsync(owner, preset.Name, revision);
        }
    }
}
