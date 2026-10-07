using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Commands;

namespace AnoCore.Tests.Commands;

[TestClass]
public sealed class CommandBindingSynchronizerTests
{
    [TestMethod]
    public void ReconcilesAddedRemovedAndReplacedCommandsWithoutDuplicateNativeBindings()
    {
        var registry = new CommandRegistry(new Permissions());
        var created = new List<CommandDescriptor>();
        var removed = new List<string>();
        using var synchronizer = new CommandBindingSynchronizer(registry, descriptor =>
        {
            created.Add(descriptor);
            return new Handle(() => removed.Add(descriptor.Name));
        });
        using var first = registry.Register(new ModuleId("first"), new CommandDescriptor("anoexternal", "First"),
            _ => ValueTask.FromResult(CommandResult.Ok()));
        synchronizer.Synchronize();
        synchronizer.Synchronize();
        Assert.HasCount(1, created);
        first.Dispose();
        using var second = registry.Register(new ModuleId("second"), new CommandDescriptor("anoexternal", "Replacement"),
            _ => ValueTask.FromResult(CommandResult.Ok()));
        synchronizer.Synchronize();
        Assert.HasCount(2, created);
        Assert.HasCount(1, removed);
        synchronizer.Dispose();
        synchronizer.Synchronize();
        Assert.HasCount(2, removed);
    }

    [TestMethod]
    public void FailedNativeBindingCanRetryAndFailingObserversDoNotRollBackRegistryChanges()
    {
        var registry = new CommandRegistry(new Permissions());
        var observations = 0;
        registry.Changed += () => throw new InvalidOperationException("observer");
        registry.Changed += () => observations++;
        using var registration = registry.Register(new ModuleId("first"), new CommandDescriptor("anoexternal", "First"),
            _ => ValueTask.FromResult(CommandResult.Ok()));
        var fail = true;
        using var synchronizer = new CommandBindingSynchronizer(registry, _ => fail
            ? throw new InvalidOperationException("native") : new Handle(() => { }));
        Assert.ThrowsExactly<InvalidOperationException>(() => synchronizer.Synchronize());
        fail = false;
        synchronizer.Synchronize();
        registration.Dispose();
        synchronizer.Synchronize();
        Assert.AreEqual(2, observations);
        Assert.IsEmpty(registry.GetCommands());
    }

    private sealed class Handle(Action dispose) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) dispose(); }
    }

    private sealed class Permissions : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }
}
