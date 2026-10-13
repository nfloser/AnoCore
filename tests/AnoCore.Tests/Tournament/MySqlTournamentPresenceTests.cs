using AnoCore.Modules.Tournament;
using AnoCore.Modules.Tournament.Persistence;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.Tournament;

[TestClass]
[DoNotParallelize]
public sealed class MySqlTournamentPresenceTests
{
    [TestMethod]
    public async Task IndependentServersPublishReadyStateAndFenceDuplicateServerIds()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("MariaDB not configured.");
        var database = new MySqlDatabase(connection!);
        await TournamentPersistenceBootstrap.EnsureReadyAsync(database);
        var repository = new MySqlTournamentPresenceRepository(database);
        var prefix = "presence-" + Guid.NewGuid().ToString("N")[..8];
        var instance = Guid.NewGuid();
        var session = Guid.NewGuid();
        var player = new TournamentPlayerPresence(123, session, "Player", Guid.NewGuid(), true);
        await repository.PublishAsync(prefix + "-a", instance, [player]);
        await repository.PublishAsync(prefix + "-b", Guid.NewGuid(), [player with { SteamId = 456, IsReady = false }]);
        var reader = new MySqlTournamentPresenceRepository(new MySqlDatabase(connection!));
        var states = await reader.LoadOnlineAsync();
        Assert.AreEqual(2, states.Count(state => state.ServerId.StartsWith(prefix, StringComparison.Ordinal)));
        Assert.IsTrue(states.Single(state => state.ServerId == prefix + "-a").Players.Single().IsReady);
        await Assert.ThrowsExactlyAsync<TournamentServerIdentityConflictException>(async () =>
            await repository.PublishAsync(prefix + "-a", Guid.NewGuid(), []));
        await repository.PublishAsync(prefix + "-a", instance, [player with { SessionId = Guid.NewGuid(), MatchId = null, IsReady = false }]);
        Assert.IsFalse((await reader.LoadOnlineAsync()).Single(state => state.ServerId == prefix + "-a").Players.Single().IsReady);
        await database.WithConnectionAsync(async (db, token) =>
        {
            await using var command = db.CreateCommand();
            command.CommandText = "UPDATE ano_tournament_server_presence SET expires_at_utc = UTC_TIMESTAMP(6) - INTERVAL 1 SECOND WHERE server_id = @id";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.Value = prefix + "-a"; command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync(token);
            return true;
        });
        Assert.IsFalse((await reader.LoadOnlineAsync()).Any(state => state.ServerId == prefix + "-a"));
        await reader.PublishAsync(prefix + "-a", Guid.NewGuid(), []);
        Assert.IsEmpty((await reader.LoadOnlineAsync()).Single(state => state.ServerId == prefix + "-a").Players);
    }

    [TestMethod]
    public void PresenceRejectsDuplicateSessionsAndReadyWithoutMatch()
    {
        var player = new TournamentPlayerPresence(123, Guid.NewGuid(), "Player", null, false);
        Assert.ThrowsExactly<ArgumentException>(() => TournamentPresenceValidation.RequireValid("server", Guid.NewGuid(), [player, player]));
        Assert.ThrowsExactly<ArgumentException>(() => TournamentPresenceValidation.RequireValid("server", Guid.NewGuid(), [player with { IsReady = true }]));
        Assert.ThrowsExactly<ArgumentException>(() => TournamentPresenceValidation.RequireValid("../server", Guid.NewGuid(), []));
        Assert.ThrowsExactly<ArgumentException>(() => TournamentPresenceValidation.RequireValid("server", Guid.Empty, []));
    }
}
