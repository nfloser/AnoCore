using System.Data;
using System.Data.Common;
using AnoCore.Abstractions.Management;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Management;
using AnoCore.Runtime.Modules;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Management;

[TestClass]
public sealed class RuntimeManagementStatusProviderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 20, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task StatusProvider_ExposesSafeHealthServerAndPlayerState()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new PlayerConnection(
            new PlayerId(76561198000206001),
            "Name\nInjected",
            PlayerTeam.CounterTerrorist,
            true,
            Now));

        var modules = new ModuleHost(new ModuleContext(new EmptyServices()));
        var provider = new RuntimeManagementStatusProvider(
            new FakeDatabase(ping: true),
            players,
            modules,
            new FixedTime(Now));

        var health = await provider.GetHealthAsync();
        var server = await provider.GetServerAsync();
        var playerRows = await provider.GetPlayersAsync();

        Assert.IsTrue(health.Ready);
        Assert.AreEqual("ready", health.RuntimeStatus);
        Assert.AreEqual(ManagementApiVersion.Current, server.ApiVersion);
        Assert.AreEqual(AnoCoreApi.CurrentLevel, server.ModuleApiLevel);
        Assert.AreEqual(1, server.ConnectedPlayers);
        Assert.AreEqual(1, playerRows.Count);
        Assert.AreEqual("Name Injected", playerRows[0].DisplayName);
        Assert.AreEqual("CounterTerrorist", playerRows[0].Team);
    }

    [TestMethod]
    public async Task Health_FailsClosedWithoutLeakingDatabaseException()
    {
        var provider = new RuntimeManagementStatusProvider(
            new FakeDatabase(new InvalidOperationException("password=secret")),
            new PlayerRegistry(new AnoEventBus()),
            new ModuleHost(new ModuleContext(new EmptyServices())),
            new FixedTime(Now));

        var health = await provider.GetHealthAsync();

        Assert.IsFalse(health.Ready);
        Assert.AreEqual("database_unavailable", health.RuntimeStatus);
    }

    [TestMethod]
    public async Task Modules_ExposeStateButNotFailureDetails()
    {
        var modules = new ModuleHost(new ModuleContext(new EmptyServices()));
        try
        {
            await modules.LoadAsync(new FailingModule());
        }
        catch (InvalidOperationException)
        {
        }

        var provider = new RuntimeManagementStatusProvider(
            new FakeDatabase(true),
            new PlayerRegistry(new AnoEventBus()),
            modules,
            new FixedTime(Now));

        var result = await provider.GetModulesAsync();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("test.failing", result[0].ModuleId.Value);
        Assert.AreEqual("Faulted", result[0].State);
        Assert.IsFalse(result[0].ToString().Contains(
            "super-secret", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FailingModule : IAnoModule
    {
        public ModuleDescriptor Descriptor { get; } =
            new(new ModuleId("test.failing"), "Failing", "1.0.0", "Test");

        public Task InitializeAsync(
            IAnoModuleContext context,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("super-secret");

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeDatabase : IDatabase
    {
        private readonly bool _ping;
        private readonly Exception? _exception;

        public FakeDatabase(bool ping)
        {
            _ping = ping;
        }

        public FakeDatabase(Exception exception)
        {
            _exception = exception;
        }

        public ValueTask<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _exception is null
                ? ValueTask.FromResult(_ping)
                : ValueTask.FromException<bool>(_exception);
        }

        public ValueTask<TResult> WithConnectionAsync<TResult>(
            Func<DbConnection, CancellationToken, ValueTask<TResult>> action,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<TResult> InTransactionAsync<TResult>(
            Func<DbConnection, DbTransaction, CancellationToken, ValueTask<TResult>> action,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
