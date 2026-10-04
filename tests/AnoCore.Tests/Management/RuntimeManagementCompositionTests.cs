using AnoCore.Abstractions.Configuration;
using AnoCore.Abstractions.Management;
using AnoCore.Runtime.Composition;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Management;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Management;

[TestClass]
[DoNotParallelize]
public sealed class RuntimeManagementCompositionTests
{
    [TestMethod]
    public async Task Runtime_ExposesManagementStatusAndCapabilityRegistry()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");

        var configPath = Path.Combine(
            Path.GetTempPath(),
            "ano-management-composition-" + Guid.NewGuid().ToString("N"));
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);

        using var runtime = await RuntimeServices.CreateAsync(
            new MySqlDatabase(connection!),
            new JsonConfigStore(configPath),
            events,
            players);

        Assert.AreSame(
            runtime.ManagementStatus,
            runtime.GetService(typeof(IManagementStatusProvider)));
        Assert.AreSame(
            runtime.ManagementCapabilities,
            runtime.GetService(typeof(IManagementCapabilityRegistry)));
        Assert.IsInstanceOfType<RuntimeManagementStatusProvider>(
            runtime.ManagementStatus);
        Assert.IsInstanceOfType<ManagementCapabilityRegistry>(
            runtime.ManagementCapabilities);
    }
}
