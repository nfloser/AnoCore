using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class PlaytimeModuleTests
{
    private static readonly PlayerId Player = new(76561198000011301);
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ConnectHeartbeatReconnectDisconnect_UsesSessionIdentity()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var repository = new MemoryRepository();
        using var module = await PlaytimeModule.CreateAsync(events, players, repository);
        var first = await players.ConnectAsync(new PlayerConnection(Player, "A", PlayerTeam.Terrorist, true, Start));
        await module.CheckpointOnlineAsync(Start.AddSeconds(30));
        var second = await players.ConnectAsync(new PlayerConnection(Player, "A", PlayerTeam.CounterTerrorist,
            true, Start.AddMinutes(1)));
        await players.DisconnectAsync(Player, first.SessionId, Start.AddMinutes(2));
        await players.DisconnectAsync(Player, second.SessionId, Start.AddMinutes(2));

        CollectionAssert.AreEqual(new[] {
            (first.SessionId, Start.AddSeconds(30), false),
            (first.SessionId, Start.AddMinutes(1), true),
            (second.SessionId, Start.AddMinutes(2), true),
        }, repository.Advances.ToArray());
    }

    [TestMethod]
    public async Task BootstrapAndDispose_AvoidDuplicateSubscriptions()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var current = await players.ConnectAsync(new PlayerConnection(Player, "A", PlayerTeam.Terrorist, true, Start));
        var repository = new MemoryRepository();
        var module = await PlaytimeModule.CreateAsync(events, players, repository);
        Assert.AreEqual(current.SessionId, repository.Opened.Single());
        module.Dispose();
        await players.DisconnectAsync(Player, current.SessionId, Start.AddMinutes(1));
        Assert.AreEqual(0, repository.Advances.Count);
    }

    private sealed class MemoryRepository : IPlaytimeRepository
    {
        public List<PlayerSessionId> Opened { get; } = [];
        public List<(PlayerSessionId, DateTimeOffset, bool)> Advances { get; } = [];
        public ValueTask OpenAsync(PlayerId playerId, PlayerSessionId sessionId, DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken = default)
        {
            Opened.Add(sessionId);
            return ValueTask.CompletedTask;
        }
        public ValueTask AdvanceAsync(PlayerId playerId, PlayerSessionId sessionId, DateTimeOffset atUtc,
            bool close = false, CancellationToken cancellationToken = default)
        {
            Advances.Add((sessionId, atUtc, close));
            return ValueTask.CompletedTask;
        }
        public ValueTask<PlaytimeTotals> ReadAsync(PlayerId playerId, DateOnly utcDay,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new PlaytimeTotals(TimeSpan.Zero, TimeSpan.Zero));
    }
}
