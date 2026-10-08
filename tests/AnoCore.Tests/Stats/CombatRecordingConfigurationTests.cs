using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class CombatRecordingConfigurationTests
{
    [TestMethod]
    public void DefaultsEnableBoundedShotAndDamageBatches()
    {
        var configuration = new CombatRecordingConfiguration();
        Assert.IsTrue(configuration.BatchWrites && configuration.RecordWeaponFire && configuration.RecordDamage);
        Assert.AreEqual(0, CombatRecordingConfiguration.Validate(configuration).Count);
    }

    [TestMethod]
    public void InvalidBoundsAndNonfiniteIntervalsAreRejected()
    {
        foreach (var configuration in new CombatRecordingConfiguration[]
        {
            new() { BatchSize = 0 }, new() { BatchSize = 257 },
            new() { Capacity = 255 }, new() { Capacity = 65537 },
            new() { FlushIntervalSeconds = double.NaN }, new() { FlushIntervalSeconds = double.PositiveInfinity },
            new() { FlushIntervalSeconds = 0 }, new() { FlushIntervalSeconds = 31 },
            new() { ShutdownTimeoutSeconds = 0 }, new() { ShutdownTimeoutSeconds = 31 },
        })
            Assert.IsNotEmpty(CombatRecordingConfiguration.Validate(configuration));
    }
}
