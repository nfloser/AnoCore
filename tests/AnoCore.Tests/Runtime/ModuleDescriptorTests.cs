using AnoCore.Abstractions.Modules;

namespace AnoCore.Tests.Runtime;

[TestClass]
public sealed class ModuleDescriptorTests
{
    [TestMethod]
    public void ExistingConstructor_DefaultsToOldestSupportedApiLevel()
    {
        var descriptor = new ModuleDescriptor(
            new ModuleId("ano.compat"),
            "Compatibility",
            "1.0.0",
            "Uses the legacy constructor.");

        Assert.AreEqual(AnoCoreApi.MinimumSupportedLevel, descriptor.MinimumApiLevel);
    }

    [TestMethod]
    public void Constructor_StoresExplicitMinimumApiLevel()
    {
        var descriptor = new ModuleDescriptor(
            new ModuleId("ano.current"),
            "Current API",
            "1.0.0",
            "Declares an API requirement.",
            AnoCoreApi.CurrentLevel);

        Assert.AreEqual(AnoCoreApi.CurrentLevel, descriptor.MinimumApiLevel);
    }

    [TestMethod]
    public void Constructor_RejectsNonPositiveMinimumApiLevel()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new ModuleDescriptor(
                new ModuleId("ano.invalid"),
                "Invalid API",
                "1.0.0",
                "Invalid requirement.",
                0));
    }
}
