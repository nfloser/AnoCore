using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Stats;

public sealed class MySqlCombatRepository : ICombatDetailRepository
{
    private readonly IDatabase _database;

    public MySqlCombatRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public async ValueTask RecordAsync(CombatDeath death, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(death);
        await _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO ano_combat_deaths (
                    event_id, victim_steam_id, attacker_steam_id, assister_steam_id,
                    occurred_at_utc, is_team_kill)
                VALUES (@id, @victim, @attacker, @assister, @occurred, @team)
                ON DUPLICATE KEY UPDATE event_id = event_id
                """;
            Add(insert, "@id", death.EventId.ToString("D"));
            Add(insert, "@victim", death.VictimId.SteamId64);
            Add(insert, "@attacker", Steam(death.AttackerId));
            Add(insert, "@assister", Steam(death.AssisterId));
            Add(insert, "@occurred", death.OccurredAtUtc.UtcDateTime);
            Add(insert, "@team", death.IsTeamKill);
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);

            await using var verify = connection.CreateCommand();
            verify.Transaction = transaction;
            verify.CommandText = """
                SELECT victim_steam_id, attacker_steam_id, assister_steam_id,
                    occurred_at_utc, is_team_kill
                FROM ano_combat_deaths WHERE event_id = @id FOR UPDATE
                """;
            Add(verify, "@id", death.EventId.ToString("D"));
            await using var reader = await verify.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)
                || ReadPlayer(reader, 0) != death.VictimId
                || ReadPlayer(reader, 1) != death.AttackerId
                || ReadPlayer(reader, 2) != death.AssisterId
                || reader.GetBoolean(4) != death.IsTeamKill)
            {
                throw new InvalidOperationException("Combat event id conflicts with a different death.");
            }

            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<CombatTotals> ReadAsync(PlayerId playerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM ano_effective_combat_kills
                     WHERE attacker_steam_id = @player),
                    (SELECT COUNT(*) FROM ano_effective_combat_deaths
                     WHERE victim_steam_id = @player),
                    (SELECT COUNT(*) FROM ano_effective_combat_assists
                     WHERE assister_steam_id = @player)
                """;
            Add(command, "@player", playerId.SteamId64);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                throw new InvalidOperationException("Combat aggregate query returned no row.");
            return new CombatTotals(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
        }, cancellationToken);
    }

    public async ValueTask RecordWeaponFireAsync(CombatWeaponFireEvent weaponFire,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(weaponFire);
        await _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO ano_combat_weapon_fire (
                        event_id, player_steam_id, occurred_at_utc, map_name, weapon)
                    VALUES (@id, @player, @occurred, @map, @weapon)
                    ON DUPLICATE KEY UPDATE event_id = event_id
                    """;
                Add(insert, "@id", weaponFire.EventId.ToString("D"));
                Add(insert, "@player", weaponFire.PlayerId.SteamId64);
                Add(insert, "@occurred", weaponFire.OccurredAtUtc.UtcDateTime);
                Add(insert, "@map", weaponFire.MapName);
                Add(insert, "@weapon", weaponFire.Weapon);
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await using var verify = connection.CreateCommand();
            verify.Transaction = transaction;
            verify.CommandText = """
                SELECT player_steam_id, occurred_at_utc, map_name, weapon
                FROM ano_combat_weapon_fire
                WHERE event_id = @id
                FOR UPDATE
                """;
            Add(verify, "@id", weaponFire.EventId.ToString("D"));
            await using var reader = await verify.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)
                || ReadPlayer(reader, 0) != weaponFire.PlayerId
                || !string.Equals(reader.GetString(2), weaponFire.MapName, StringComparison.Ordinal)
                || !string.Equals(reader.GetString(3), weaponFire.Weapon, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Combat weapon-fire event id conflicts with a different event.");
            }

            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RecordDamageAsync(CombatDamageEvent damage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(damage);
        await _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO ano_combat_damage (
                        event_id, victim_steam_id, attacker_steam_id, occurred_at_utc,
                        map_name, weapon, hitgroup, damage_health, damage_armor, is_team_damage)
                    VALUES (
                        @id, @victim, @attacker, @occurred,
                        @map, @weapon, @hitgroup, @health, @armor, @team)
                    ON DUPLICATE KEY UPDATE event_id = event_id
                    """;
                Add(insert, "@id", damage.EventId.ToString("D"));
                Add(insert, "@victim", damage.VictimId.SteamId64);
                Add(insert, "@attacker", Steam(damage.AttackerId));
                Add(insert, "@occurred", damage.OccurredAtUtc.UtcDateTime);
                Add(insert, "@map", damage.MapName);
                Add(insert, "@weapon", damage.Weapon);
                Add(insert, "@hitgroup", damage.Hitgroup);
                Add(insert, "@health", damage.DamageHealth);
                Add(insert, "@armor", damage.DamageArmor);
                Add(insert, "@team", damage.IsTeamDamage);
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await using var verify = connection.CreateCommand();
            verify.Transaction = transaction;
            verify.CommandText = """
                SELECT victim_steam_id, attacker_steam_id, occurred_at_utc,
                    map_name, weapon, hitgroup, damage_health, damage_armor, is_team_damage
                FROM ano_combat_damage
                WHERE event_id = @id
                FOR UPDATE
                """;
            Add(verify, "@id", damage.EventId.ToString("D"));
            await using var reader = await verify.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)
                || ReadPlayer(reader, 0) != damage.VictimId
                || ReadPlayer(reader, 1) != damage.AttackerId
                || !string.Equals(reader.GetString(3), damage.MapName, StringComparison.Ordinal)
                || !string.Equals(reader.GetString(4), damage.Weapon, StringComparison.Ordinal)
                || reader.GetInt32(5) != damage.Hitgroup
                || reader.GetInt32(6) != damage.DamageHealth
                || reader.GetInt32(7) != damage.DamageArmor
                || reader.GetBoolean(8) != damage.IsTeamDamage)
            {
                throw new InvalidOperationException(
                    "Combat damage event id conflicts with a different event.");
            }

            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<CombatDetailTotals> ReadDetailsAsync(PlayerId playerId,
        CombatDetailFilter? filter = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        filter ??= new CombatDetailFilter();
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    (SELECT COUNT(*)
                     FROM ano_effective_combat_weapon_fire
                     WHERE player_steam_id = @player
                       AND (@map IS NULL OR map_name = @map)
                       AND (@weapon IS NULL OR weapon = @weapon)),
                    (SELECT COUNT(*)
                     FROM ano_effective_combat_damage
                     WHERE attacker_steam_id = @player
                       AND (@map IS NULL OR map_name = @map)
                       AND (@weapon IS NULL OR weapon = @weapon)
                       AND (@include_team = 1 OR is_team_damage = 0)
                       AND (@include_self = 1 OR victim_steam_id <> @player)),
                    (SELECT COALESCE(SUM(damage_health), 0)
                     FROM ano_effective_combat_damage
                     WHERE attacker_steam_id = @player
                       AND (@map IS NULL OR map_name = @map)
                       AND (@weapon IS NULL OR weapon = @weapon)
                       AND (@include_team = 1 OR is_team_damage = 0)
                       AND (@include_self = 1 OR victim_steam_id <> @player)),
                    (SELECT COALESCE(SUM(damage_armor), 0)
                     FROM ano_effective_combat_damage
                     WHERE attacker_steam_id = @player
                       AND (@map IS NULL OR map_name = @map)
                       AND (@weapon IS NULL OR weapon = @weapon)
                       AND (@include_team = 1 OR is_team_damage = 0)
                       AND (@include_self = 1 OR victim_steam_id <> @player)),
                    (SELECT COUNT(*)
                     FROM ano_effective_combat_damage
                     WHERE attacker_steam_id = @player
                       AND hitgroup = 1
                       AND (@map IS NULL OR map_name = @map)
                       AND (@weapon IS NULL OR weapon = @weapon)
                       AND (@include_team = 1 OR is_team_damage = 0)
                       AND (@include_self = 1 OR victim_steam_id <> @player))
                """;
            AddDetailFilter(command, playerId, filter);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                throw new InvalidOperationException("Combat detail aggregate query returned no row.");
            return new CombatDetailTotals(
                Long(reader.GetValue(0)),
                Long(reader.GetValue(1)),
                Long(reader.GetValue(2)),
                Long(reader.GetValue(3)),
                Long(reader.GetValue(4)));
        }, cancellationToken);
    }

    public ValueTask<IReadOnlyList<CombatHitgroupTotals>> ReadHitgroupsAsync(PlayerId playerId,
        CombatDetailFilter? filter = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        filter ??= new CombatDetailFilter();
        return _database.WithConnectionAsync<IReadOnlyList<CombatHitgroupTotals>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT hitgroup, COUNT(*),
                    COALESCE(SUM(damage_health), 0),
                    COALESCE(SUM(damage_armor), 0)
                FROM ano_effective_combat_damage
                WHERE attacker_steam_id = @player
                  AND (@map IS NULL OR map_name = @map)
                  AND (@weapon IS NULL OR weapon = @weapon)
                  AND (@include_team = 1 OR is_team_damage = 0)
                  AND (@include_self = 1 OR victim_steam_id <> @player)
                GROUP BY hitgroup
                ORDER BY hitgroup ASC
                """;
            AddDetailFilter(command, playerId, filter);
            var result = new List<CombatHitgroupTotals>();
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                result.Add(new CombatHitgroupTotals(
                    reader.GetInt32(0),
                    Long(reader.GetValue(1)),
                    Long(reader.GetValue(2)),
                    Long(reader.GetValue(3))));
            }
            return result;
        }, cancellationToken);
    }

    public ValueTask<IReadOnlyList<CombatRankEntry>> GetTopKillsAsync(int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(offset));
        return _database.WithConnectionAsync<IReadOnlyList<CombatRankEntry>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT ranked.attacker_steam_id, ranked.kills, profiles.last_known_name
                FROM (
                    SELECT attacker_steam_id, COUNT(*) AS kills
                    FROM ano_effective_combat_kills
                    WHERE attacker_steam_id IS NOT NULL
                    GROUP BY attacker_steam_id
                ) AS ranked
                LEFT JOIN ano_players AS profiles ON profiles.steam_id = ranked.attacker_steam_id
                ORDER BY ranked.kills DESC, ranked.attacker_steam_id ASC
                LIMIT @limit OFFSET @offset
                """;
            Add(command, "@limit", limit);
            Add(command, "@offset", offset);
            var entries = new List<CombatRankEntry>();
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                entries.Add(new CombatRankEntry(
                    new PlayerId(Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture)),
                    reader.GetInt64(1), offset + entries.Count + 1,
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            return entries;
        }, cancellationToken);
    }

    public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopDeathsAsync(int limit, int offset,
        CancellationToken cancellationToken = default)
        => GetTopCountAsync("victim_steam_id", limit, offset, cancellationToken);

    public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopAssistsAsync(int limit, int offset,
        CancellationToken cancellationToken = default)
        => GetTopCountAsync("assister_steam_id", limit, offset, cancellationToken);

    private ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopCountAsync(
        string column, int limit, int offset, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(offset));
        // Only the two fixed, internal column names above may reach this SQL template.
        var view = column == "victim_steam_id"
            ? "ano_effective_combat_deaths"
            : "ano_effective_combat_assists";
        return _database.WithConnectionAsync<IReadOnlyList<CombatCountRankEntry>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT ranked.steam_id, ranked.total, profiles.last_known_name
                FROM (
                    SELECT {column} AS steam_id, COUNT(*) AS total
                    FROM {view}
                    WHERE {column} IS NOT NULL
                    GROUP BY {column}
                ) AS ranked
                LEFT JOIN ano_players AS profiles ON profiles.steam_id = ranked.steam_id
                ORDER BY ranked.total DESC, ranked.steam_id ASC
                LIMIT @limit OFFSET @offset
                """;
            Add(command, "@limit", limit);
            Add(command, "@offset", offset);
            var entries = new List<CombatCountRankEntry>();
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                entries.Add(new CombatCountRankEntry(
                    new PlayerId(Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture)),
                    reader.GetInt64(1), offset + entries.Count + 1,
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            return entries;
        }, cancellationToken);
    }

    public ValueTask<IReadOnlyList<CombatScoreRankEntry>> GetTopScoresAsync(
        int killPoints, int assistPoints, int deathPenalty, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        if (killPoints is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(killPoints));
        if (assistPoints is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(assistPoints));
        if (deathPenalty is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(deathPenalty));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(offset));
        return _database.WithConnectionAsync<IReadOnlyList<CombatScoreRankEntry>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                WITH players AS (
                    SELECT victim_steam_id AS steam_id FROM ano_effective_combat_deaths
                    UNION
                    SELECT attacker_steam_id FROM ano_effective_combat_kills
                        WHERE attacker_steam_id IS NOT NULL
                    UNION
                    SELECT assister_steam_id FROM ano_effective_combat_assists
                        WHERE assister_steam_id IS NOT NULL
                    UNION
                    SELECT player_steam_id FROM ano_rank_adjustments
                ), scored AS (
                    SELECT players.steam_id,
                        GREATEST(0,
                            CAST((SELECT COUNT(*) FROM ano_effective_combat_kills
                                WHERE attacker_steam_id = players.steam_id) AS SIGNED)
                                * @kill_points
                            + CAST((SELECT COUNT(*) FROM ano_effective_combat_assists
                                WHERE assister_steam_id = players.steam_id) AS SIGNED) * @assist_points
                            - CAST((SELECT COUNT(*) FROM ano_effective_combat_deaths
                                WHERE victim_steam_id = players.steam_id) AS SIGNED) * @death_penalty
                            + COALESCE(adjustments.points, 0)
                        ) AS points
                    FROM players
                    LEFT JOIN ano_rank_adjustments AS adjustments
                        ON adjustments.player_steam_id = players.steam_id
                )
                SELECT scored.steam_id, scored.points, profiles.last_known_name
                FROM scored
                LEFT JOIN ano_players AS profiles ON profiles.steam_id = scored.steam_id
                ORDER BY scored.points DESC, scored.steam_id ASC
                LIMIT @limit OFFSET @offset
                """;
            Add(command, "@kill_points", killPoints);
            Add(command, "@assist_points", assistPoints);
            Add(command, "@death_penalty", deathPenalty);
            Add(command, "@limit", limit);
            Add(command, "@offset", offset);
            var entries = new List<CombatScoreRankEntry>();
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                entries.Add(new CombatScoreRankEntry(
                    new PlayerId(Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture)),
                    reader.GetInt64(1), offset + entries.Count + 1,
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            return entries;
        }, cancellationToken);
    }

    public ValueTask<CombatScoreRankEntry?> GetScorePlacementAsync(PlayerId playerId,
        int killPoints, int assistPoints, int deathPenalty,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (killPoints is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(killPoints));
        if (assistPoints is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(assistPoints));
        if (deathPenalty is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(deathPenalty));
        return _database.WithConnectionAsync<CombatScoreRankEntry?>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                WITH players AS (
                    SELECT victim_steam_id AS steam_id FROM ano_effective_combat_deaths
                    UNION
                    SELECT attacker_steam_id FROM ano_effective_combat_kills
                        WHERE attacker_steam_id IS NOT NULL
                    UNION
                    SELECT assister_steam_id FROM ano_effective_combat_assists
                        WHERE assister_steam_id IS NOT NULL
                    UNION
                    SELECT player_steam_id FROM ano_rank_adjustments
                ), scored AS (
                    SELECT players.steam_id,
                        GREATEST(0,
                            CAST((SELECT COUNT(*) FROM ano_effective_combat_kills
                                WHERE attacker_steam_id = players.steam_id) AS SIGNED)
                                * @kill_points
                            + CAST((SELECT COUNT(*) FROM ano_effective_combat_assists
                                WHERE assister_steam_id = players.steam_id) AS SIGNED) * @assist_points
                            - CAST((SELECT COUNT(*) FROM ano_effective_combat_deaths
                                WHERE victim_steam_id = players.steam_id) AS SIGNED) * @death_penalty
                            + COALESCE(adjustments.points, 0)
                        ) AS points
                    FROM players
                    LEFT JOIN ano_rank_adjustments AS adjustments
                        ON adjustments.player_steam_id = players.steam_id
                ), ranked AS (
                    SELECT steam_id, points,
                        ROW_NUMBER() OVER (ORDER BY points DESC, steam_id ASC) AS position
                    FROM scored
                )
                SELECT ranked.steam_id, ranked.points, ranked.position, profiles.last_known_name
                FROM ranked
                LEFT JOIN ano_players AS profiles ON profiles.steam_id = ranked.steam_id
                WHERE ranked.steam_id = @player
                """;
            Add(command, "@kill_points", killPoints);
            Add(command, "@assist_points", assistPoints);
            Add(command, "@death_penalty", deathPenalty);
            Add(command, "@player", playerId.SteamId64);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
            return new CombatScoreRankEntry(
                new PlayerId(Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture)),
                reader.GetInt64(1), Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
                reader.IsDBNull(3) ? null : reader.GetString(3));
        }, cancellationToken);
    }

    private static void AddDetailFilter(
        DbCommand command, PlayerId playerId, CombatDetailFilter filter)
    {
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@map", filter.MapName is null ? DBNull.Value : filter.MapName);
        Add(command, "@weapon", filter.Weapon is null ? DBNull.Value : filter.Weapon);
        Add(command, "@include_team", filter.IncludeTeamDamage ? 1 : 0);
        Add(command, "@include_self", filter.IncludeSelfDamage ? 1 : 0);
    }

    private static DateTimeOffset ReadUtc(DbDataReader reader, int index)
        => new(DateTime.SpecifyKind(reader.GetDateTime(index), DateTimeKind.Utc));

    private static long Long(object value)
        => Convert.ToInt64(value, CultureInfo.InvariantCulture);

    private static PlayerId? ReadPlayer(DbDataReader reader, int index)
        => reader.IsDBNull(index) ? null
            : new PlayerId(Convert.ToUInt64(reader.GetValue(index), CultureInfo.InvariantCulture));
    private static object Steam(PlayerId? id) => id is null ? DBNull.Value : id.SteamId64;
    private static void Add(DbCommand command, string name, object value)
        => MigrationRunner.AddParameter(command, name, value);
}
