using AnoCore.Abstractions.Modules;
using AnoCore.Runtime.Modules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Runtime;

[TestClass]
public sealed class ModuleHostTests
{
    [TestMethod]
    public async Task LoadAsync_InitializesModuleAndMarksItLoaded()
    {
        var module = new FakeModule("ano.test");
        var host = new ModuleHost(new TestModuleContext());

        await host.LoadAsync(module);

        Assert.AreEqual(1, module.InitializeCalls);
        Assert.AreEqual(ModuleState.Loaded, host.GetState(module.Descriptor.Id));
        Assert.HasCount(1, host.Modules);
    }

    [TestMethod]
    public async Task LoadAsync_RejectsDuplicateActiveModuleId()
    {
        var host = new ModuleHost(new TestModuleContext());
        await host.LoadAsync(new FakeModule("ano.duplicate"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => host.LoadAsync(new FakeModule("ANO.DUPLICATE")));
    }

    [TestMethod]
    public async Task LoadAsync_WhenInitializationFails_MarksModuleFaultedAndRethrows()
    {
        var module = new FakeModule("ano.failure")
        {
            InitializeException = new InvalidOperationException("boom"),
        };
        var host = new ModuleHost(new TestModuleContext());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.LoadAsync(module));

        Assert.AreEqual(ModuleState.Faulted, host.GetState(module.Descriptor.Id));
    }

    [TestMethod]
    public async Task UnloadAsync_ShutsDownLoadedModuleAndMarksItUnloaded()
    {
        var module = new FakeModule("ano.unload");
        var host = new ModuleHost(new TestModuleContext());
        await host.LoadAsync(module);

        var unloaded = await host.UnloadAsync(module.Descriptor.Id);

        Assert.IsTrue(unloaded);
        Assert.AreEqual(1, module.ShutdownCalls);
        Assert.AreEqual(ModuleState.Unloaded, host.GetState(module.Descriptor.Id));
    }

    [TestMethod]
    public async Task UnloadAsync_ReturnsFalseForUnknownModule()
    {
        var host = new ModuleHost(new TestModuleContext());

        var unloaded = await host.UnloadAsync(new ModuleId("ano.unknown"));

        Assert.IsFalse(unloaded);
    }

    private sealed class FakeModule(string id) : IAnoModule
    {
        public ModuleDescriptor Descriptor { get; } = new(
            new ModuleId(id),
            "Test module",
            "1.0.0",
            "Test-only module");

        public Exception? InitializeException { get; init; }

        public int InitializeCalls { get; private set; }

        public int ShutdownCalls { get; private set; }

        public Task InitializeAsync(IAnoModuleContext context, CancellationToken cancellationToken = default)
        {
            InitializeCalls++;

            if (InitializeException is not null)
            {
                throw InitializeException;
            }

            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            ShutdownCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestModuleContext : IAnoModuleContext
    {
        public IServiceProvider Services { get; } = new EmptyServiceProvider();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
