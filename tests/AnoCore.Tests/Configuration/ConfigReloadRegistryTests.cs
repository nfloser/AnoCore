using AnoCore.Abstractions.Modules;
using AnoCore.Runtime.Configuration;

namespace AnoCore.Tests.Configuration;

[TestClass]
public sealed class ConfigReloadRegistryTests
{
    [TestMethod]
    public async Task ReloadAsync_PublishesOnlyValidatedCompletedValue()
    {
        var registry = new ConfigReloadRegistry();
        var next = 2;
        using var registration = registry.Register(
            new ModuleId("tests"), "tests.feature", 1,
            _ => ValueTask.FromResult(next),
            value => value > 0 ? [] : ["Must be positive."]);

        await registry.ReloadAsync("tests.feature");
        Assert.AreEqual(2, registration.Current);

        next = 0;
        await Assert.ThrowsExactlyAsync<ConfigValidationException>(async () =>
            await registry.ReloadAsync("tests.feature"));
        Assert.AreEqual(2, registration.Current);
    }

    [TestMethod]
    public async Task DisposeDuringReload_PreventsDelayedPublicationAndRemovesDescriptor()
    {
        var registry = new ConfigReloadRegistry();
        var completion = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = registry.Register(
            new ModuleId("tests"), "tests.delayed", 1,
            async _ => await completion.Task);
        var reload = registry.ReloadAsync("tests.delayed").AsTask();

        registration.Dispose();
        completion.SetResult(2);

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await reload);
        Assert.HasCount(0, registry.Configurations);
    }

    [TestMethod]
    public async Task ConcurrentReloads_AreSerialized()
    {
        var registry = new ConfigReloadRegistry();
        var active = 0;
        var maximum = 0;
        using var registration = registry.Register(
            new ModuleId("tests"), "tests.serial", 0,
            async token =>
            {
                var current = Interlocked.Increment(ref active);
                maximum = Math.Max(maximum, current);
                await Task.Delay(10, token);
                Interlocked.Decrement(ref active);
                return registrationValue++;
            });
        registrationValue = 1;

        await Task.WhenAll(
            registry.ReloadAsync("tests.serial").AsTask(),
            registry.ReloadAsync("tests.serial").AsTask());

        Assert.AreEqual(1, maximum);
        Assert.AreEqual(2, registration.Current);
    }

    [TestMethod]
    public void Register_RejectsDuplicateAndDisposalReleasesName()
    {
        var registry = new ConfigReloadRegistry();
        var owner = new ModuleId("tests");
        var first = registry.Register(owner, "tests.same", 1, _ => ValueTask.FromResult(2));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            registry.Register(owner, "tests.same", 1, _ => ValueTask.FromResult(2)));

        first.Dispose();
        using var replacement = registry.Register(
            owner, "tests.same", 3, _ => ValueTask.FromResult(4));
        Assert.AreEqual(3, replacement.Current);
    }

    private static int registrationValue;
}
