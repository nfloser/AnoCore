using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Runtime.Players;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Players;

[TestClass]
public sealed class PlayerRegistryTests
{
    private static readonly DateTimeOffset FirstSeen = new(2026, 9, 16, 16, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ConnectAsync_AddsPlayerAndPublishesConnectedEvent()
    {
        var events = new RecordingEventBus();
        var registry = new PlayerRegistry(events);
        var id = new PlayerId(76561198000000001);

        var player = await registry.ConnectAsync(new PlayerConnection(
            id,
            "  Nille  ",
            PlayerTeam.CounterTerrorist,
            isAlive: true,
            FirstSeen));

        Assert.AreEqual(id, player.Id);
        Assert.AreEqual("Nille", player.Name);
        Assert.IsTrue(player.IsConnected);
        Assert.IsTrue(player.IsAlive);
        Assert.AreEqual(PlayerTeam.CounterTerrorist, player.Team);
        Assert.AreEqual(FirstSeen, player.ConnectedAtUtc);
        Assert.AreEqual(FirstSeen, player.LastUpdatedAtUtc);
        Assert.AreNotEqual(Guid.Empty, player.SessionId.Value);
        Assert.HasCount(1, registry.OnlinePlayers);
        Assert.IsTrue(registry.TryGet(id, out var fromRegistry));
        Assert.AreEqual(player, fromRegistry);
        Assert.HasCount(1, events.Published);

        var connected = Assert.IsInstanceOfType<PlayerConnectedEvent>(events.Published[0]);
        Assert.AreEqual(player, connected.Player);
    }

    [TestMethod]
    public async Task ConnectAsync_BlankNameFallsBackToUnknown()
    {
        var registry = new PlayerRegistry(new RecordingEventBus());

        var player = await registry.ConnectAsync(new PlayerConnection(
            new PlayerId(76561198000000002),
            "   ",
            PlayerTeam.Unknown,
            isAlive: false,
            FirstSeen));

        Assert.AreEqual("Unknown", player.Name);
    }

    [TestMethod]
    public async Task ConnectAsync_ReconnectReplacesSessionWithoutDuplicatingPlayer()
    {
        var events = new RecordingEventBus();
        var registry = new PlayerRegistry(events);
        var id = new PlayerId(76561198000000003);
        var first = await registry.ConnectAsync(new PlayerConnection(
            id,
            "Player",
            PlayerTeam.Terrorist,
            isAlive: true,
            FirstSeen));

        var secondSeen = FirstSeen.AddMinutes(2);
        var second = await registry.ConnectAsync(new PlayerConnection(
            id,
            "Player Reconnected",
            PlayerTeam.CounterTerrorist,
            isAlive: false,
            secondSeen));

        Assert.AreNotEqual(first.SessionId, second.SessionId);
        Assert.HasCount(1, registry.OnlinePlayers);
        Assert.IsTrue(registry.TryGet(id, out var current));
        Assert.AreEqual(second, current);
        Assert.HasCount(2, events.Published);

        var reconnected = Assert.IsInstanceOfType<PlayerReconnectedEvent>(events.Published[1]);
        Assert.AreEqual(first, reconnected.Previous);
        Assert.AreEqual(second, reconnected.Current);
    }

    [TestMethod]
    public async Task DisconnectAsync_StaleSessionDoesNotRemoveReconnectedPlayer()
    {
        var events = new RecordingEventBus();
        var registry = new PlayerRegistry(events);
        var id = new PlayerId(76561198000000004);
        var first = await registry.ConnectAsync(new PlayerConnection(
            id,
            "Player",
            PlayerTeam.Terrorist,
            isAlive: true,
            FirstSeen));
        var second = await registry.ConnectAsync(new PlayerConnection(
            id,
            "Player",
            PlayerTeam.Terrorist,
            isAlive: true,
            FirstSeen.AddSeconds(10)));

        var disconnected = await registry.DisconnectAsync(
            id,
            first.SessionId,
            FirstSeen.AddSeconds(20));

        Assert.IsNull(disconnected);
        Assert.IsTrue(registry.TryGet(id, out var current));
        Assert.AreEqual(second, current);
        Assert.AreEqual(2, events.Published.Count);
    }

    [TestMethod]
    public async Task DisconnectAsync_CurrentSessionRemovesPlayerAndPublishesEvent()
    {
        var events = new RecordingEventBus();
        var registry = new PlayerRegistry(events);
        var id = new PlayerId(76561198000000005);
        var connected = await registry.ConnectAsync(new PlayerConnection(
            id,
            "Player",
            PlayerTeam.Spectator,
            isAlive: false,
            FirstSeen));
        var disconnectedAt = FirstSeen.AddMinutes(5);

        var disconnected = await registry.DisconnectAsync(id, connected.SessionId, disconnectedAt);

        Assert.IsNotNull(disconnected);
        Assert.IsFalse(disconnected.IsConnected);
        Assert.AreEqual(disconnectedAt, disconnected.LastUpdatedAtUtc);
        Assert.IsFalse(registry.TryGet(id, out _));
        Assert.HasCount(0, registry.OnlinePlayers);

        var disconnectedEvent = Assert.IsInstanceOfType<PlayerDisconnectedEvent>(events.Published[1]);
        Assert.AreEqual(disconnected, disconnectedEvent.Player);
    }

    [TestMethod]
    public async Task UpdateAsync_UpdatesMatchingSessionAndPublishesChange()
    {
        var events = new RecordingEventBus();
        var registry = new PlayerRegistry(events);
        var id = new PlayerId(76561198000000006);
        var connected = await registry.ConnectAsync(new PlayerConnection(
            id,
            "Old Name",
            PlayerTeam.Spectator,
            isAlive: false,
            FirstSeen));
        var updatedAt = FirstSeen.AddSeconds(30);

        var updated = await registry.UpdateAsync(new PlayerStateUpdate(
            id,
            connected.SessionId,
            " New Name ",
            PlayerTeam.CounterTerrorist,
            isAlive: true,
            updatedAt));

        Assert.IsNotNull(updated);
        Assert.AreEqual(connected.SessionId, updated.SessionId);
        Assert.AreEqual("New Name", updated.Name);
        Assert.AreEqual(PlayerTeam.CounterTerrorist, updated.Team);
        Assert.IsTrue(updated.IsAlive);
        Assert.AreEqual(updatedAt, updated.LastUpdatedAtUtc);

        var changed = Assert.IsInstanceOfType<PlayerUpdatedEvent>(events.Published[1]);
        Assert.AreEqual(connected, changed.Previous);
        Assert.AreEqual(updated, changed.Current);
    }

    [TestMethod]
    public async Task UpdateAsync_StaleSessionDoesNotChangeCurrentPlayer()
    {
        var events = new RecordingEventBus();
        var registry = new PlayerRegistry(events);
        var id = new PlayerId(76561198000000007);
        var first = await registry.ConnectAsync(new PlayerConnection(
            id,
            "First",
            PlayerTeam.Terrorist,
            isAlive: true,
            FirstSeen));
        var current = await registry.ConnectAsync(new PlayerConnection(
            id,
            "Current",
            PlayerTeam.CounterTerrorist,
            isAlive: true,
            FirstSeen.AddMinutes(1)));

        var updated = await registry.UpdateAsync(new PlayerStateUpdate(
            id,
            first.SessionId,
            "Stale",
            PlayerTeam.Spectator,
            isAlive: false,
            FirstSeen.AddMinutes(2)));

        Assert.IsNull(updated);
        Assert.IsTrue(registry.TryGet(id, out var afterUpdate));
        Assert.AreEqual(current, afterUpdate);
        Assert.AreEqual(2, events.Published.Count);
    }

    [TestMethod]
    public void PlayerSessionId_RejectsEmptyGuid()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _ = new PlayerSessionId(Guid.Empty));
    }

    private sealed class RecordingEventBus : IAnoEventBus
    {
        public List<IAnoEvent> Published { get; } = [];

        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler)
            where TEvent : IAnoEvent
            => NoOpDisposable.Instance;

        public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IAnoEvent
        {
            Published.Add(@event);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static NoOpDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
