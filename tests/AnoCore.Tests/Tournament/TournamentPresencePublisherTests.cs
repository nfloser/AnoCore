using AnoCore.Abstractions.Players;
using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentPresencePublisherTests
{
    [TestMethod]
    public async Task OnlineAndReadyAreSeparateAndReconnectReplacesSession()
    {
        var repository = new Capture();
        using var publisher = new TournamentPresencePublisher(repository, "server");
        var player = new PlayerSnapshot(new(123), PlayerSessionId.New(), "Player", true, true, PlayerTeam.Terrorist,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await publisher.PublishAsync([player], null);
        Assert.IsFalse(repository.Players.Single().IsReady);
        Assert.IsNull(repository.Players.Single().MatchId);
        var match = new TournamentRecoverySnapshot(Guid.NewGuid(), TournamentMatchState.Ready, [], 0, 0, 0,
            [player.Id], null, null, PlayerTeam.Terrorist, PlayerTeam.CounterTerrorist, null);
        await publisher.PublishAsync([player], match);
        Assert.IsTrue(repository.Players.Single().IsReady);
        var reconnected = new PlayerSnapshot(player.Id, PlayerSessionId.New(), player.Name, true, true, player.Team,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await publisher.PublishAsync([reconnected], null);
        Assert.AreEqual(reconnected.SessionId.Value, repository.Players.Single().SessionId);
        Assert.IsFalse(repository.Players.Single().IsReady);
        await publisher.PublishAsync([], null);
        Assert.IsEmpty(repository.Players);
    }
    [TestMethod]
    public async Task ControlOnlyAndLongNamesCannotInvalidateTheServerHeartbeat()
    {
        var repository = new Capture();
        using var publisher = new TournamentPresencePublisher(repository, "server");
        var player = new PlayerSnapshot(new(123), PlayerSessionId.New(), "\u0001", true, true, PlayerTeam.Terrorist,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await publisher.PublishAsync([player], null);
        Assert.AreEqual("Unknown", repository.Players.Single().Name);
        var named = new PlayerSnapshot(player.Id, player.SessionId, new string('x', 200), true, true, player.Team,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await publisher.PublishAsync([named], null);
        Assert.AreEqual(128, repository.Players.Single().Name.Length);
    }

    private sealed class Capture : ITournamentPresenceRepository
    {
        public IReadOnlyCollection<TournamentPlayerPresence> Players { get; private set; } = [];
        public ValueTask PublishAsync(string serverId, Guid instanceId, IReadOnlyCollection<TournamentPlayerPresence> players, CancellationToken cancellationToken = default)
        {
            TournamentPresenceValidation.RequireValid(serverId, instanceId, players);
            Players = players;
            return ValueTask.CompletedTask;
        }
        public ValueTask<IReadOnlyList<TournamentServerPresence>> LoadOnlineAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<TournamentServerPresence>>([]);
    }
}
