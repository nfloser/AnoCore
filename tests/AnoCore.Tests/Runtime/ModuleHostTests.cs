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
    public async Task LoadAsync_WhenInitializationFails_MarksModuleFaultedAndRollsBack()
    {
        var module = new FakeModule("ano.failure")
        {
            InitializeException = new InvalidOperationException("boom"),
        };
        var host = new ModuleHost(new TestModuleContext());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.LoadAsync(module));

        Assert.AreEqual(1, module.ShutdownCalls);
        Assert.AreEqual(ModuleState.Faulted, host.GetState(module.Descriptor.Id));
    }

    [TestMethod]
    public async Task LoadAsync_WhenInitializationAndRollbackFail_RecordsBothFailures()
    {
        var module = new FakeModule("ano.double-failure")
        {
            InitializeException = new InvalidOperationException("init"),
            ShutdownException = new InvalidOperationException("rollback"),
        };
        var host = new ModuleHost(new TestModuleContext());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.LoadAsync(module));

        var snapshot = host.Modules.Single(item => item.Descriptor.Id == module.Descriptor.Id);
        var aggregate = snapshot.Failure as AggregateException;
        Assert.IsNotNull(aggregate);
        Assert.HasCount(2, aggregate.InnerExceptions);
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
    public async Task UnloadAsync_DisposesOwnedResourcesInReverseOrderAfterShutdown()
    {
        var order = new List<string>();
        var module = new FakeModule("ano.owned")
        {
            Lifecycle = order,
            OwnedResourceNames = ["first", "second"],
        };
        var host = new ModuleHost(new TestModuleContext());
        await host.LoadAsync(module);

        await host.UnloadAsync(module.Descriptor.Id);

        CollectionAssert.AreEqual(
            new[] { "shutdown", "dispose:second", "dispose:first" },
            order);
    }

    [TestMethod]
    public async Task LoadAsync_FailureShutsDownThenDisposesPartiallyOwnedResources()
    {
        var order = new List<string>();
        var module = new FakeModule("ano.owned-failure")
        {
            Lifecycle = order,
            OwnedResourceNames = ["created"],
            InitializeException = new InvalidOperationException("init"),
        };
        var host = new ModuleHost(new TestModuleContext());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.LoadAsync(module));

        CollectionAssert.AreEqual(new[] { "shutdown", "dispose:created" }, order);
        Assert.AreEqual(ModuleState.Faulted, host.GetState(module.Descriptor.Id));
    }

    [TestMethod]
    public async Task UnloadAsync_ShutdownAndCleanupFailuresAreBothRecorded()
    {
        var order = new List<string>();
        var module = new FakeModule("ano.cleanup-failure")
        {
            Lifecycle = order,
            OwnedResourceNames = ["broken"],
            ShutdownException = new InvalidOperationException("shutdown"),
            CleanupException = new InvalidOperationException("cleanup"),
        };
        var host = new ModuleHost(new TestModuleContext());
        await host.LoadAsync(module);

        await Assert.ThrowsExactlyAsync<AggregateException>(
            () => host.UnloadAsync(module.Descriptor.Id));

        var snapshot = host.Modules.Single(item => item.Descriptor.Id == module.Descriptor.Id);
        var aggregate = snapshot.Failure as AggregateException;
        Assert.IsNotNull(aggregate);
        Assert.HasCount(2, aggregate.InnerExceptions);
        CollectionAssert.AreEqual(new[] { "shutdown", "dispose:broken" }, order);
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

        public Exception? ShutdownException { get; init; }

        public int InitializeCalls { get; private set; }

        public int ShutdownCalls { get; private set; }

        public List<string>? Lifecycle { get; init; }

        public string[] OwnedResourceNames { get; init; } = [];

        public Exception? CleanupException { get; init; }

        public Task InitializeAsync(IAnoModuleContext context, CancellationToken cancellationToken = default)
        {
            InitializeCalls++;
            foreach (var name in OwnedResourceNames)
                context.Own(new CallbackDisposable(name, Lifecycle, CleanupException));

            if (InitializeException is not null)
            {
                throw InitializeException;
            }

            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            ShutdownCalls++;
            Lifecycle?.Add("shutdown");

            if (ShutdownException is not null)
            {
                throw ShutdownException;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CallbackDisposable(
        string name, List<string>? lifecycle, Exception? exception) : IDisposable
    {
        public void Dispose()
        {
            lifecycle?.Add($"dispose:{name}");
            if (exception is not null)
                throw exception;
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
