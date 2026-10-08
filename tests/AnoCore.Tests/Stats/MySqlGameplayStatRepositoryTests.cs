using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;
using AnoCore.Runtime.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
[DoNotParallelize]
public sealed class MySqlGameplayStatRepositoryTests
{
    private static readonly PlayerId Player = new(76561198000184011);
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 16, 30, 0, TimeSpan.Zero);
    private MySqlDatabase _database = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection))
            Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured for integration tests.");

        _database = new MySqlDatabase(connection!);
        await DropAsync();
        await new MigrationRunner(_database, [
            new CoreSchemaMigration001(), new ModerationSchemaMigration002(),
            new AdminAuditSchemaMigration003(), new WarningSchemaMigration004(),
            new PlaytimeSchemaMigration005(), new CombatSchemaMigration006(),
            new RankAdjustmentSchemaMigration007(), new PlaytimeStateSchemaMigration008(),
            new CombatDetailSchemaMigration009(), new GameplayStatSchemaMigration010(),
            new StatisticsResetSchemaMigration011(), new CombatDeathContextSchemaMigration020(), new StatisticsLeaderboardIndexMigration021()])
            .ApplyPendingAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_database is not null) await DropAsync();
    }

    [TestMethod]
    public async Task GameplayRankMonitor_RefreshesAfterCommitAndIsolatesPresentationFailures()
    {
        var gameplay = new MySqlGameplayStatRepository(_database);
        var ranks = new MySqlCombatRepository(_database);
        var configuration = new RankConfiguration
        {
            GameplayPoints = new() { [GameplayStatKind.Mvp] = 10 },
        };
        var sink = new RankSink();
        var errors = new List<Exception>();
        using var monitor = new GameplayRankTransitionMonitor(configuration, ranks, gameplay,
            sink, sink, errors.Add);
        var statistic = new GameplayStatEvent(Guid.NewGuid(), Player, Now,
            "de_dust2", GameplayStatKind.Mvp);
        await monitor.RecordAsync(statistic);
        Assert.AreEqual(1, sink.Notifications);
        Assert.AreEqual(1, sink.Refreshes);
        Assert.AreEqual(2, errors.Count);
        await monitor.RecordAsync(statistic);
        Assert.AreEqual(1, sink.Notifications);
        Assert.AreEqual(10L, (await ranks.GetScorePlacementAsync(Player,
            configuration.ScoreWeights))!.Points);
    }

    private sealed class RankSink : IRankTransitionNotificationSink, IRankScoreChangeSink
    {
        public int Notifications { get; private set; }
        public int Refreshes { get; private set; }
        public ValueTask NotifyAsync(PlayerId playerId, RankTransition transition,
            CancellationToken cancellationToken = default)
        {
            Notifications++;
            throw new InvalidOperationException("Disconnected presentation sink.");
        }
        public ValueTask ScoreChangedAsync(PlayerId playerId,
            CancellationToken cancellationToken = default)
        {
            Refreshes++;
            throw new InvalidOperationException("Disconnected refresh sink.");
        }
    }

    [TestMethod]
    public async Task RankWeights_IncludeGameplayOnlyPlayersAndReplayWithoutDuplicatingPoints()
    {
        var gameplay = new MySqlGameplayStatRepository(_database);
        var ranks = new MySqlCombatRepository(_database);
        var other = new PlayerId(Player.SteamId64 + 1);
        var weights = new RankScoreWeights(2, 1, 1,
            new Dictionary<GameplayStatKind, int>
            {
                [GameplayStatKind.Mvp] = 5,
                [GameplayStatKind.HostageKilled] = -3,
            });
        var bonus = new GameplayStatEvent(Guid.NewGuid(), Player, Now,
            "de_dust2", GameplayStatKind.Mvp, 2);
        await gameplay.RecordAsync(bonus);
        await gameplay.RecordAsync(bonus);
        await gameplay.RecordAsync(new GameplayStatEvent(Guid.NewGuid(), other, Now,
            "de_dust2", GameplayStatKind.Mvp, 2));
        var top = await ranks.GetTopScoresAsync(weights, 10, 0);
        Assert.AreEqual(2, top.Count);
        Assert.AreEqual(Player, top[0].PlayerId);
        Assert.AreEqual(10L, top[0].Points);
        Assert.AreEqual(2, (await ranks.GetScorePlacementAsync(other, weights))!.Position);
        Assert.AreEqual(10L, await ranks.ReadRawScoreAsync(Player, weights));
        Assert.IsEmpty(await ranks.GetTopScoresAsync(2, 1, 1, 10, 0));
        await gameplay.RecordAsync(new GameplayStatEvent(Guid.NewGuid(), Player, Now,
            "de_dust2", GameplayStatKind.HostageKilled, 4));
        Assert.AreEqual(-2L, await ranks.ReadRawScoreAsync(Player, weights));
        Assert.AreEqual(0L, (await ranks.GetScorePlacementAsync(Player, weights))!.Points);
        Assert.AreEqual(other, (await ranks.GetTopScoresAsync(weights, 1, 0))[0].PlayerId);
        await new MySqlStatisticsResetAdministrationService(_database).ResetAsync(
            Player, null, "rank integration reset", Now.AddSeconds(1));
        Assert.IsNull(await ranks.GetScorePlacementAsync(Player, weights));
        await gameplay.RecordAsync(bonus);
        Assert.IsNull(await ranks.GetScorePlacementAsync(Player, weights));
        await gameplay.RecordAsync(new GameplayStatEvent(Guid.NewGuid(), Player, Now.AddSeconds(2),
            "de_dust2", GameplayStatKind.Mvp));
        Assert.AreEqual(5L, (await ranks.GetScorePlacementAsync(Player, weights))!.Points);
    }

    [TestMethod]
    public async Task Replay_IsIdempotentAndConflictingPayloadFailsAfterRestart()
    {
        var repository = new MySqlGameplayStatRepository(_database);
        var stat = new GameplayStatEvent(Guid.NewGuid(), Player, Now,
            "de_dust2", GameplayStatKind.BombPlanted);

        await repository.RecordAsync(stat);
        await repository.RecordAsync(new GameplayStatEvent(
            stat.EventId, Player, Now.AddMilliseconds(20),
            "de_dust2", GameplayStatKind.BombPlanted));

        var restarted = new MySqlGameplayStatRepository(_database);
        var totals = await restarted.ReadAsync(Player);
        Assert.AreEqual(1L, totals.Single(x =>
            x.Kind == GameplayStatKind.BombPlanted).Count);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await restarted.RecordAsync(new GameplayStatEvent(
                stat.EventId, Player, Now, "de_dust2", GameplayStatKind.BombDefused)));
    }

    [TestMethod]
    public async Task Read_GroupsKindsAndFiltersByMap()
    {
        var repository = new MySqlGameplayStatRepository(_database);
        await repository.RecordAsync(new GameplayStatEvent(
            Guid.NewGuid(), Player, Now, "de_dust2", GameplayStatKind.GrenadeThrown));
        await repository.RecordAsync(new GameplayStatEvent(
            Guid.NewGuid(), Player, Now.AddSeconds(1), "de_dust2",
            GameplayStatKind.GrenadeThrown, 2));
        await repository.RecordAsync(new GameplayStatEvent(
            Guid.NewGuid(), Player, Now.AddSeconds(2), "de_nuke", GameplayStatKind.Mvp));

        var all = await repository.ReadAsync(Player);
        Assert.AreEqual(3L, all.Single(x =>
            x.Kind == GameplayStatKind.GrenadeThrown).Count);
        Assert.AreEqual(1L, all.Single(x => x.Kind == GameplayStatKind.Mvp).Count);

        var dust = await repository.ReadAsync(
            Player, new GameplayStatFilter(" de_dust2 "));
        Assert.AreEqual(1, dust.Count);
        Assert.AreEqual(GameplayStatKind.GrenadeThrown, dust.Single().Kind);
        Assert.AreEqual(3L, dust.Single().Count);
    }

    [TestMethod]
    public async Task StatisticsMenu_RatesRequireSamplesAndTiesUseStableOfflinePlayerIdentity()
    {
        var combat = new MySqlCombatRepository(_database);
        var second = new PlayerId(Player.SteamId64 + 1);
        var shortSample = new PlayerId(Player.SteamId64 + 2);
        var victim = new PlayerId(Player.SteamId64 + 10);
        var profiles = new MySqlPlayerRepository(_database);
        foreach (var player in new[] { Player, second, shortSample })
        {
            await profiles.UpsertAsync(new PlayerProfile(player, "Offline " + player.SteamId64, Now, Now));
            var count = player == shortSample ? 49 : 50;
            for (var index = 0; index < count; index++)
                await combat.RecordAsync(new CombatDeath(Guid.NewGuid(), victim, player, null, Now)
                {
                    Context = new("de_dust2", "weapon_ak47", PlayerTeam.Terrorist,
                        index % 2 == 0, false, false, 0, null),
                });
            for (var index = 0; index < 20; index++)
                await combat.RecordAsync(new CombatDeath(Guid.NewGuid(), player, null, null, Now));
        }
        var repository = new MySqlStatisticsMenuRepository(_database);
        foreach (var category in new[] { StatisticsCategory.KillDeathRatio, StatisticsCategory.HeadshotPercentage })
        {
            var all = await repository.GetTopAsync(category, 10, 0);
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual(Player, all[0].PlayerId);
            Assert.AreEqual(second, all[1].PlayerId);
            Assert.IsNotNull(all[1].DisplayName);
            var next = await repository.GetTopAsync(category, 1, 1);
            Assert.AreEqual(second, next.Single().PlayerId);
            Assert.AreEqual(2, next.Single().Position);
            Assert.AreEqual(category == StatisticsCategory.KillDeathRatio ? 2.5m : 50m, next.Single().Value);
        }
        var own = await repository.ReadPersonalAsync(Player);
        Assert.AreEqual(50L, own.Combat.Kills);
        Assert.AreEqual(20L, own.Combat.Deaths);
        Assert.AreEqual(25L, own.Headshots);
        Assert.AreEqual(50L, own.NativeKills);
    }

    [TestMethod]
    public async Task StatisticsMenu_MatchMinimumAndGameplayCategoriesRespectResets()
    {
        var gameplay = new MySqlGameplayStatRepository(_database);
        var second = new PlayerId(Player.SteamId64 + 1);
        var shortSample = new PlayerId(Player.SteamId64 + 2);
        foreach (var player in new[] { Player, second, shortSample })
        {
            await gameplay.RecordAsync(new(Guid.NewGuid(), player, Now, "de_nuke", GameplayStatKind.MatchWon, player == shortSample ? 9 : 8));
            if (player != shortSample)
                await gameplay.RecordAsync(new(Guid.NewGuid(), player, Now, "de_nuke", GameplayStatKind.MatchLost, 2));
            foreach (var kind in new[] { GameplayStatKind.RoundWon, GameplayStatKind.Mvp, GameplayStatKind.BombPlanted,
                         GameplayStatKind.BombDefused, GameplayStatKind.GrenadeKill, GameplayStatKind.KnifeKill })
                await gameplay.RecordAsync(new(Guid.NewGuid(), player, Now, "de_nuke", kind, 3));
        }
        var repository = new MySqlStatisticsMenuRepository(_database);
        var rates = await repository.GetTopAsync(StatisticsCategory.MatchWinPercentage, 10, 0);
        Assert.AreEqual(2, rates.Count);
        Assert.AreEqual(Player, rates[0].PlayerId);
        Assert.AreEqual(80m, rates[0].Value);
        foreach (var category in new[] { StatisticsCategory.RoundWins, StatisticsCategory.Mvp, StatisticsCategory.BombPlants,
                     StatisticsCategory.BombDefuses, StatisticsCategory.GrenadeKills, StatisticsCategory.KnifeKills })
            Assert.AreEqual(3m, (await repository.GetTopAsync(category, 10, 0)).First().Value);
        var own = await repository.ReadPersonalAsync(Player);
        Assert.AreEqual(8L, own.MatchWins);
        Assert.AreEqual(2L, own.MatchLosses);
        Assert.AreEqual(3L, own.Mvp);
        await new MySqlStatisticsResetAdministrationService(_database).ResetAsync(Player, null, "statistics menu test", Now.AddSeconds(1));
        Assert.AreEqual(0L, (await repository.ReadPersonalAsync(Player)).MatchWins);
        Assert.AreEqual(second, (await repository.GetTopAsync(StatisticsCategory.MatchWinPercentage, 10, 0)).Single().PlayerId);
    }

    [TestMethod]
    public async Task StatisticsMenu_HeadshotDenominatorExcludesHistoricalUnknownContextAndTeamKills()
    {
        var combat = new MySqlCombatRepository(_database);
        var victim = new PlayerId(Player.SteamId64 + 10);
        for (var index = 0; index < 50; index++)
            await combat.RecordAsync(new(Guid.NewGuid(), victim, Player, null, Now));
        await combat.RecordAsync(new(Guid.NewGuid(), victim, Player, null, Now)
        {
            Context = new("de_dust2", "weapon_ak47", PlayerTeam.Terrorist, true, false, false, 0, null),
        });
        await combat.RecordAsync(new(Guid.NewGuid(), victim, Player, null, Now, isTeamKill: true)
        {
            Context = new("de_dust2", "weapon_ak47", PlayerTeam.Terrorist, true, false, false, 0, null),
        });
        var repository = new MySqlStatisticsMenuRepository(_database);
        var own = await repository.ReadPersonalAsync(Player);
        Assert.AreEqual(51L, own.Combat.Kills);
        Assert.AreEqual(1L, own.NativeKills);
        Assert.AreEqual(1L, own.Headshots);
        Assert.IsEmpty(await repository.GetTopAsync(StatisticsCategory.HeadshotPercentage, 10, 0));
        await new MySqlStatisticsResetAdministrationService(_database).ResetAsync(Player, null, "statistics menu test", Now.AddSeconds(1));
        Assert.AreEqual(0L, (await repository.ReadPersonalAsync(Player)).NativeKills);
    }

    [TestMethod]
    public async Task StatisticsMenu_DelegatesCountsAndPlaytimeAndBoundsPagination()
    {
        var victim = new PlayerId(Player.SteamId64 + 1);
        var assister = new PlayerId(Player.SteamId64 + 2);
        await new MySqlCombatRepository(_database).RecordAsync(new(Guid.NewGuid(), victim, Player, assister, Now));
        var playtime = new MySqlPlaytimeRepository(_database);
        var session = PlayerSessionId.New();
        await playtime.OpenAsync(Player, session, Now);
        await playtime.AdvanceAsync(Player, session, Now.AddMinutes(30), close: true);
        var repository = new MySqlStatisticsMenuRepository(_database);
        foreach (var category in new[] { StatisticsCategory.Kills, StatisticsCategory.Deaths, StatisticsCategory.Assists })
            Assert.AreEqual(1m, (await repository.GetTopAsync(category, 10, 0)).Single().Value);
        Assert.AreEqual(1800m, (await repository.GetTopAsync(StatisticsCategory.Playtime, 10, 0)).Single().Value);
        Assert.AreEqual(TimeSpan.FromMinutes(30), (await repository.ReadPersonalAsync(Player)).Playtime);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () => await repository.GetTopAsync(StatisticsCategory.Kills, 101, 0));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () => await repository.GetTopAsync(StatisticsCategory.Kills, 5, 10001));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () => await repository.GetTopAsync((StatisticsCategory)99, 5, 0));
    }

    [TestMethod]
    public async Task StatisticsMenu_IndexIsRestartSafeAndEmptyPersonalSnapshotIsDefined()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await new StatisticsLeaderboardIndexMigration021().ApplyAsync(connection, token);
            await new StatisticsLeaderboardIndexMigration021().ApplyAsync(connection, token);
            await using var inspect = connection.CreateCommand();
            inspect.CommandText = "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'ano_gameplay_stats' AND index_name = 'ix_ano_gameplay_stats_kind_player_time'";
            Assert.AreEqual(4L, Convert.ToInt64(await inspect.ExecuteScalarAsync(token)));
            return true;
        });
        var repository = new MySqlStatisticsMenuRepository(_database);
        Assert.AreEqual(new PersonalStatistics(new(0, 0, 0), TimeSpan.Zero, 0, 0, 0, 0, 0, 0), await repository.ReadPersonalAsync(Player));
        foreach (var category in Enum.GetValues<StatisticsCategory>()) Assert.IsEmpty(await repository.GetTopAsync(category, 5, 0));
    }

    private async Task DropAsync()
    {
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var view in new[] {
                "ano_effective_gameplay_stats", "ano_effective_combat_damage",
                "ano_effective_combat_weapon_fire", "ano_effective_combat_assists",
                "ano_effective_combat_deaths", "ano_effective_combat_kills"
            })
            {
                await using var dropView = connection.CreateCommand();
                dropView.CommandText = $"DROP VIEW IF EXISTS {view}";
                await dropView.ExecuteNonQueryAsync(token);
            }
            foreach (var table in new[] {
                "ano_combat_death_context",
                "ano_statistics_resets", "ano_gameplay_stats", "ano_combat_damage", "ano_combat_weapon_fire",
                "ano_rank_adjustments", "ano_playtime_segments", "ano_combat_deaths",
                "ano_playtime_sessions", "ano_admin_warnings", "ano_admin_action_audit",
                "ano_moderation_audit", "ano_moderation_sanctions", "ano_module_data",
                "ano_players", "ano_schema_migrations"
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE IF EXISTS {table}";
                await command.ExecuteNonQueryAsync(token);
            }
            return true;
        });
    }
}
