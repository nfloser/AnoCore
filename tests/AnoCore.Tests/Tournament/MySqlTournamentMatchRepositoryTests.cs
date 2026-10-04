using AnoCore.Abstractions.Players;
using AnoCore.Modules.Tournament;
using AnoCore.Modules.Tournament.Persistence;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.Tournament;

[TestClass]
[DoNotParallelize]
public sealed class MySqlTournamentMatchRepositoryTests
{
    private static readonly PlayerId A1 = new(76561198000192001);
    private static readonly PlayerId A2 = new(76561198000192002);
    private static readonly PlayerId B1 = new(76561198000192011);
    private static readonly PlayerId B2 = new(76561198000192012);
    private MySqlDatabase _database = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");

        _database = new MySqlDatabase(connection!);
        await DropAsync();
        await TournamentPersistenceBootstrap.EnsureReadyAsync(_database);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task ActiveMatch_RestoresRosterSeriesAndSnapshotAfterRestart()
    {
        var configuration = Configuration(TournamentBestOf.Three);
        var repository = new MySqlTournamentMatchRepository(_database);
        var service = new TournamentRecoveryService(repository);
        var session = await service.BeginAsync(configuration);

        session.Machine.OpenReady();
        foreach (var player in new[] { A1, A2, B1, B2 })
            session.Machine.Ready(player);
        session.Machine.BeginVeto();
        session.Machine.CompleteVeto(["de_dust2", "de_nuke", "de_inferno"]);
        session.Machine.CompleteKnife(TournamentTeamSlot.TeamB);
        session.Machine.ChooseSide(TournamentTeamSlot.TeamB, PlayerTeam.Terrorist);
        await service.SaveAsync(session);

        var restarted = new TournamentRecoveryService(
            new MySqlTournamentMatchRepository(_database));
        var restored = await restarted.RestoreActiveAsync();

        Assert.IsNotNull(restored);
        Assert.AreEqual(2L, restored.Revision);
        Assert.AreEqual(configuration.MatchId, restored.Machine.Configuration.MatchId);
        Assert.AreEqual(TournamentMatchState.Live, restored.Machine.State);
        Assert.AreEqual(PlayerTeam.CounterTerrorist, restored.Machine.AssignedSide(A1));
        Assert.AreEqual(PlayerTeam.Terrorist, restored.Machine.AssignedSide(B1));
        CollectionAssert.AreEqual(
            new[] { "de_dust2", "de_nuke", "de_inferno" },
            restored.Machine.Maps.ToArray());
        Assert.IsTrue(restored.Machine.IsReady);
    }

    [TestMethod]
    public async Task StaleSession_CannotOverwriteNewerSnapshotRevision()
    {
        var repository = new MySqlTournamentMatchRepository(_database);
        var service = new TournamentRecoveryService(repository);
        var first = await service.BeginAsync(Configuration(TournamentBestOf.One));
        var staleStored = await repository.LoadActiveAsync();
        Assert.IsNotNull(staleStored);
        var stale = new TournamentRecoverySession(
            TournamentMatchStateMachine.Restore(
                staleStored.Configuration, staleStored.Snapshot),
            staleStored.Revision);

        first.Machine.OpenReady();
        await service.SaveAsync(first);
        stale.Machine.OpenReady();

        var exception = await Assert.ThrowsExactlyAsync<TournamentConcurrencyException>(async () =>
            await service.SaveAsync(stale));
        Assert.AreEqual(1L, exception.ExpectedRevision);
        Assert.AreEqual(2L, exception.ActualRevision);

        var current = await repository.LoadActiveAsync();
        Assert.IsNotNull(current);
        Assert.AreEqual(2L, current.Revision);
        Assert.AreEqual(TournamentMatchState.Ready, current.Snapshot.State);
    }

    [TestMethod]
    public async Task CompletedMatch_DeactivationPersistsFinalStateAndClearsActivePointer()
    {
        var configuration = Configuration(TournamentBestOf.One, knifeRound: false);
        var repository = new MySqlTournamentMatchRepository(_database);
        var service = new TournamentRecoveryService(repository);
        var session = await service.BeginAsync(configuration);

        session.Machine.OpenReady();
        foreach (var player in new[] { A1, A2, B1, B2 })
            session.Machine.Ready(player);
        session.Machine.BeginVeto();
        session.Machine.CompleteVeto(["de_mirage"]);
        Assert.IsTrue(session.Machine.CompleteMap(TournamentTeamSlot.TeamA));

        await service.DeactivateAsync(session);

        Assert.AreEqual(2L, session.Revision);
        Assert.IsNull(await repository.LoadActiveAsync());
        var stored = await repository.LoadAsync(configuration.MatchId);
        Assert.IsNotNull(stored);
        Assert.AreEqual(TournamentMatchState.Completed, stored.Snapshot.State);
        Assert.AreEqual(1, stored.Snapshot.TeamAMaps);
    }

    [TestMethod]
    public async Task NewActiveMatch_ReplacesPointerWithoutDeletingPreviousMatch()
    {
        var repository = new MySqlTournamentMatchRepository(_database);
        var first = Configuration(TournamentBestOf.One);
        var second = Configuration(TournamentBestOf.Three);

        await repository.StoreAsync(
            first, new TournamentMatchStateMachine(first).Snapshot(), makeActive: true);
        await repository.StoreAsync(
            second, new TournamentMatchStateMachine(second).Snapshot(), makeActive: true);

        var active = await repository.LoadActiveAsync();
        Assert.IsNotNull(active);
        Assert.AreEqual(second.MatchId, active.Configuration.MatchId);
        Assert.IsNotNull(await repository.LoadAsync(first.MatchId));
    }

    [TestMethod]
    public async Task FailedRosterWrite_RollsBackMatchAndActivePointer()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER fail_tournament_member
                BEFORE INSERT ON ano_tournament_members
                FOR EACH ROW
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'forced member failure'
                """;
            await command.ExecuteNonQueryAsync(token);
            return true;
        });

        var repository = new MySqlTournamentMatchRepository(_database);
        var configuration = Configuration(TournamentBestOf.One);

        await Assert.ThrowsExceptionAsync<Exception>(async () =>
            await repository.StoreAsync(
                configuration,
                new TournamentMatchStateMachine(configuration).Snapshot(),
                makeActive: true));

        Assert.IsNull(await repository.LoadAsync(configuration.MatchId));
        Assert.IsNull(await repository.LoadActiveAsync());
    }

    [TestMethod]
    public async Task Bootstrap_IsIdempotent()
    {
        await TournamentPersistenceBootstrap.EnsureReadyAsync(_database);
        await TournamentPersistenceBootstrap.EnsureReadyAsync(_database);

        var configuration = Configuration(TournamentBestOf.One);
        var stored = await new MySqlTournamentMatchRepository(_database).StoreAsync(
            configuration,
            new TournamentMatchStateMachine(configuration).Snapshot(),
            makeActive: false);

        Assert.AreEqual(1L, stored.Revision);
    }

    private static TournamentMatchConfiguration Configuration(
        TournamentBestOf bestOf,
        bool knifeRound = true)
        => new(
            Guid.NewGuid(),
            bestOf,
            new TournamentTeam("Alpha", "A", A1, [A1, A2]),
            new TournamentTeam("Beta", "B", B1, [B1, B2]),
            knifeRound,
            overtimeEnabled: true);

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var statement in new[]
            {
                "DROP TRIGGER IF EXISTS fail_tournament_member",
                "DROP TABLE IF EXISTS ano_tournament_runtime",
                "DROP TABLE IF EXISTS ano_tournament_members",
                "DROP TABLE IF EXISTS ano_tournament_matches",
                "DROP TABLE IF EXISTS ano_schema_migrations",
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = statement;
                await command.ExecuteNonQueryAsync(token);
            }

            return true;
        });
    }
}
