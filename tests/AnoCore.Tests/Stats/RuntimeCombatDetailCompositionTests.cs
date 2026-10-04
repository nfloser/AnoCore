using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Composition;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Stats;

[TestClass]
[DoNotParallelize]
public sealed class RuntimeCombatDetailCompositionTests
{
    [TestMethod]
    public async Task Runtime_ExposesLegacyAndDetailCombatContracts()
    {
        var connectionString = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");

        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var configPath = Path.Combine(Path.GetTempPath(),
            "ano-combat-detail-composition-" + Guid.NewGuid().ToString("N"));
        using var runtime = await RuntimeServices.CreateAsync(
            new MySqlDatabase(connectionString!),
            new JsonConfigStore(configPath),
            events,
            players);

        Assert.AreSame(runtime.Combat, runtime.GetService(typeof(ICombatRepository)));
        Assert.AreSame(runtime.Combat, runtime.GetService(typeof(ICombatDetailRepository)));
    }
}
