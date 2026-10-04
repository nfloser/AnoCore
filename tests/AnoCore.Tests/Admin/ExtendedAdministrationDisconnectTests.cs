using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ExtendedAdministrationDisconnectTests
{
    private static readonly PlayerId Id = new(76561198000017612);
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 15, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task BlockedAdminActionCannotKeepDisconnectedSessionVisible()
    {
        var registry = new PlayerRegistry(new AnoEventBus());
        var player = await registry.ConnectAsync(Connection());
        var transport = new BlockingTransport();
        using var state = new ExtendedPlayerStateService(transport);
        var mutation = state.ApplyAsync(player,
            new ExtendedPlayerStateMutation(ExtendedPlayerStateOperation.God)).AsTask();
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task? disconnect = null;
        try
        {
            disconnect = ExtendedAdministrationDisconnect.DisconnectAsync(
                registry, state, null, player, Now).AsTask();

            Assert.IsFalse(registry.TryGet(Id, out _),
                "Disconnect must invalidate the session before waiting for admin cleanup.");
            Assert.IsFalse(disconnect.IsCompleted);

            var replacement = await registry.ConnectAsync(Connection());
            Assert.AreNotEqual(player.SessionId, replacement.SessionId);
        }
        finally
        {
            transport.Release.TrySetResult();
            await mutation.WaitAsync(TimeSpan.FromSeconds(5));
            if (disconnect is not null) await disconnect.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.IsTrue(registry.TryGet(Id, out var current));
        Assert.AreNotEqual(player.SessionId, current!.SessionId);
    }

    [TestMethod]
    public async Task ObserverFailureStillClearsDeathPositionOwnership()
    {
        var events = new AnoEventBus();
        var registry = new PlayerRegistry(events);
        var player = await registry.ConnectAsync(Connection());
        using var observer = events.Subscribe<PlayerDisconnectedEvent>(
            (_, _) => ValueTask.FromException(new InvalidOperationException("observer failed")));
        var positions = new ExtendedPositionService(new UnusedPositionTransport());
        positions.RecordDeathPosition(player, new PlayerWorldPosition(1, 2, 3));

        await Assert.ThrowsExactlyAsync<AggregateException>(() =>
            ExtendedAdministrationDisconnect.DisconnectAsync(
                registry, null, positions, player, Now).AsTask());

        Assert.IsFalse(registry.TryGet(Id, out _));
        Assert.IsFalse(positions.TryGetDeathPosition(player.SessionId, out _));
    }

    private static PlayerConnection Connection()
        => new(Id, "Target", PlayerTeam.Terrorist, true, Now);

    private sealed class BlockingTransport : IExtendedPlayerStateTransport
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ExtendedPlayerStateBaseline> CaptureAsync(
            PlayerSnapshot player, ExtendedPlayerStateFacet facet,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new ExtendedPlayerStateBaseline(facet);
        }

        public ValueTask ApplyAsync(PlayerSnapshot player, ExtendedPlayerStateMutation mutation,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask RestoreAsync(PlayerSnapshot player, ExtendedPlayerStateBaseline baseline,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class UnusedPositionTransport : IExtendedPositionTransport
    {
        public ValueTask<PlayerWorldPosition> ReadPositionAsync(PlayerSnapshot player,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask RespawnAsync(PlayerSnapshot player,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask TeleportAsync(PlayerSnapshot player, PlayerWorldPosition position,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask SlapAsync(PlayerSnapshot player, int damage,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
