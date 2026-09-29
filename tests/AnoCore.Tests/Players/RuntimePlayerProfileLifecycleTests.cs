using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Runtime.Composition;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Players;

[TestClass]
[DoNotParallelize]
public sealed class RuntimePlayerProfileLifecycleTests
{
    private static readonly DateTimeOffset ConnectedAt =
        new(2026, 9, 29, 4, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Runtime_PublishesDurableLoadedAndUnloadedEvents()
    {
        var connectionString = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");

        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var beforeStartup = await players.ConnectAsync(new PlayerConnection(
            new PlayerId(76561198000014201),
            "Bootstrap",
            PlayerTeam.Spectator,
            isAlive: false,
            ConnectedAt));
        var loaded = new List<PlayerProfileLoadedEvent>();
        var unloaded = new List<PlayerProfileUnloadedEvent>();
        using var loadedSubscription = events.Subscribe<PlayerProfileLoadedEvent>((value, _) =>
        {
            loaded.Add(value);
            return ValueTask.CompletedTask;
        });
        using var unloadedSubscription = events.Subscribe<PlayerProfileUnloadedEvent>((value, _) =>
        {
            unloaded.Add(value);
            return ValueTask.CompletedTask;
        });
        var configPath = Path.Combine(
            Path.GetTempPath(), "ano-profile-events-" + Guid.NewGuid().ToString("N"));

        using var runtime = await RuntimeServices.CreateAsync(
            new MySqlDatabase(connectionString!),
            new JsonConfigStore(configPath),
            events,
            players);

        Assert.HasCount(1, loaded);
        Assert.AreEqual(beforeStartup.SessionId, loaded[0].Player.SessionId);
        Assert.AreEqual("Bootstrap", loaded[0].Profile.LastKnownName);
        Assert.IsNotNull(await runtime.Profiles.GetAsync(beforeStartup.Id));

        var connected = await players.ConnectAsync(new PlayerConnection(
            new PlayerId(76561198000014202),
            "After startup",
            PlayerTeam.Terrorist,
            isAlive: true,
            ConnectedAt.AddMinutes(1)));
        Assert.HasCount(2, loaded);
        Assert.AreEqual(connected.SessionId, loaded[1].Player.SessionId);
        Assert.IsNotNull(await runtime.Profiles.GetAsync(connected.Id));

        await players.DisconnectAsync(
            connected.Id,
            connected.SessionId,
            ConnectedAt.AddMinutes(2));

        Assert.HasCount(1, unloaded);
        Assert.AreEqual(connected.SessionId, unloaded[0].Player.SessionId);
        var stored = await runtime.Profiles.GetAsync(connected.Id);
        Assert.IsNotNull(stored);
        Assert.AreEqual(ConnectedAt.AddMinutes(2), stored.LastSeenUtc);
    }

    [TestMethod]
    public async Task LoadedObserverFailure_DoesNotFailCommittedConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");

        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        using var failure = events.Subscribe<PlayerProfileLoadedEvent>(
            (_, _) => throw new InvalidOperationException("observer"));
        var configPath = Path.Combine(
            Path.GetTempPath(), "ano-profile-failure-" + Guid.NewGuid().ToString("N"));
        using var runtime = await RuntimeServices.CreateAsync(
            new MySqlDatabase(connectionString!),
            new JsonConfigStore(configPath),
            events,
            players);
        var id = new PlayerId(76561198000014203);

        var connected = await players.ConnectAsync(new PlayerConnection(
            id,
            "Observer failure",
            PlayerTeam.CounterTerrorist,
            isAlive: true,
            ConnectedAt));

        Assert.AreEqual(id, connected.Id);
        Assert.IsNotNull(await runtime.Profiles.GetAsync(id));
    }
}
