using AnoCore.Abstractions.Targeting;
using AnoCore.Runtime.Composition;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Targeting;

[TestClass]
[DoNotParallelize]
public sealed class RuntimeTargetingCompositionTests
{
    [TestMethod]
    public async Task RuntimeServices_ExposesSingleSharedTargetingServices()
    {
        var connectionString = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");
        }

        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var configPath = Path.Combine(Path.GetTempPath(), "ano-targeting-" + Guid.NewGuid().ToString("N"));
        using var runtime = await RuntimeServices.CreateAsync(
            new MySqlDatabase(connectionString!),
            new JsonConfigStore(configPath),
            events,
            players);

        Assert.AreSame(runtime.TargetResolver, runtime.GetService(typeof(IPlayerTargetResolver)));
        Assert.AreSame(runtime.TargetAuthorization, runtime.GetService(typeof(ITargetAuthorizationService)));
    }
}
