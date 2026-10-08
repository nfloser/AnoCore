using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Stats;

public sealed class MySqlCombatRepository : ICombatDetailRepository, ICombatDetailBatchRepository, IGameplayRankScoreRepository
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

            await reader.DisposeAsync().ConfigureAwait(false);
            if (death.Context is { } context)
                await WriteDeathContextAsync(connection, transaction, death.EventId, context, token).ConfigureAwait(false);
            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteDeathContextAsync(DbConnection connection, DbTransaction transaction,
        Guid eventId, CombatDeathContext context, CancellationToken token)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO ano_combat_death_context
                (event_id, map_name, weapon, attacker_team, headshot, no_scope, through_smoke,
                 penetrations, distance_meters, attacker_blind)
            VALUES (@id, @map, @weapon, @team, @headshot, @noscope, @smoke, @penetrations, @distance, @blind)
            ON DUPLICATE KEY UPDATE event_id = event_id
            """;
        Add(insert, "@id", eventId.ToString("D"));
        Add(insert, "@map", context.MapName);
        Add(insert, "@weapon", context.Weapon);
        Add(insert, "@team", (byte)context.AttackerTeam);
        Add(insert, "@headshot", context.Headshot);
        Add(insert, "@noscope", context.NoScope);
        Add(insert, "@smoke", context.ThroughSmoke);
        Add(insert, "@penetrations", context.Penetrations);
        Add(insert, "@distance", context.DistanceMeters is { } distance ? distance : DBNull.Value);
        Add(insert, "@blind", context.AttackerBlind);
        await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await using var verify = connection.CreateCommand();
        verify.Transaction = transaction;
        verify.CommandText = """
            SELECT map_name, weapon, attacker_team, headshot, no_scope, through_smoke,
                penetrations, distance_meters, attacker_blind
            FROM ano_combat_death_context WHERE event_id = @id FOR UPDATE
            """;
        Add(verify, "@id", eventId.ToString("D"));
        await using var reader = await verify.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)
            || reader.GetString(0) != context.MapName || reader.GetString(1) != context.Weapon
            || Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) != (int)context.AttackerTeam
            || reader.GetBoolean(3) != context.Headshot || reader.GetBoolean(4) != context.NoScope
            || reader.GetBoolean(5) != context.ThroughSmoke
            || Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture) != context.Penetrations
            || (reader.IsDBNull(7) ? (decimal?)null : reader.GetDecimal(7)) != context.DistanceMeters
            || reader.GetBoolean(8) != context.AttackerBlind)
            throw new InvalidOperationException("Combat event id conflicts with different native death context.");
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

    public ValueTask RecordWeaponFireAsync(CombatWeaponFireEvent weaponFire,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(weaponFire);
        return RecordDetailsAsync(new CombatDetailBatch([weaponFire], []), cancellationToken);
    }

    public ValueTask RecordDamageAsync(CombatDamageEvent damage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(damage);
        return RecordDetailsAsync(new CombatDetailBatch([], [damage]), cancellationToken);
    }

    public async ValueTask RecordDetailsAsync(CombatDetailBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();
        if (batch.Count == 0) return;
        var shots = new Dictionary<Guid, CombatWeaponFireEvent>();
        var hits = new Dictionary<Guid, CombatDamageEvent>();
        foreach (var shot in batch.WeaponFire)
        {
            if (shots.TryGetValue(shot.EventId, out var previous) && !SameShot(previous, shot))
                throw new InvalidOperationException("Combat weapon-fire event id conflicts within the batch.");
            shots.TryAdd(shot.EventId, shot);
        }
        foreach (var hit in batch.Damage)
        {
            if (hits.TryGetValue(hit.EventId, out var previous) && !SameDamage(previous, hit))
                throw new InvalidOperationException("Combat damage event id conflicts within the batch.");
            hits.TryAdd(hit.EventId, hit);
        }

        await _database.InTransactionAsync(async (connection, transaction, token) =>
        {
            if (shots.Count > 0) await WriteShotsAsync(connection, transaction,
                shots.Values.OrderBy(value => value.EventId).ToArray(), token).ConfigureAwait(false);
            if (hits.Count > 0) await WriteDamageAsync(connection, transaction,
                hits.Values.OrderBy(value => value.EventId).ToArray(), token).ConfigureAwait(false);
            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteShotsAsync(DbConnection connection, DbTransaction transaction,
        IReadOnlyList<CombatWeaponFireEvent> shots, CancellationToken token)
    {
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            var rows = new List<string>();
            for (var index = 0; index < shots.Count; index++)
            {
                var shot = shots[index];
                var suffix = index.ToString(CultureInfo.InvariantCulture);
                rows.Add($"(@id{suffix}, @player{suffix}, @occurred{suffix}, @map{suffix}, @weapon{suffix})");
                Add(insert, "@id" + suffix, shot.EventId.ToString("D"));
                Add(insert, "@player" + suffix, shot.PlayerId.SteamId64);
                Add(insert, "@occurred" + suffix, shot.OccurredAtUtc.UtcDateTime);
                Add(insert, "@map" + suffix, shot.MapName);
                Add(insert, "@weapon" + suffix, shot.Weapon);
            }
            insert.CommandText = "INSERT INTO ano_combat_weapon_fire "
                + "(event_id, player_steam_id, occurred_at_utc, map_name, weapon) VALUES "
                + string.Join(",", rows) + " ON DUPLICATE KEY UPDATE event_id = event_id";
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        await using var verify = connection.CreateCommand();
        verify.Transaction = transaction;
        verify.CommandText = "SELECT event_id, player_steam_id, map_name, weapon "
            + "FROM ano_combat_weapon_fire WHERE event_id IN (" + AddIds(verify, shots.Select(value => value.EventId))
            + ") FOR UPDATE";
        var expected = shots.ToDictionary(value => value.EventId);
        await using var reader = await verify.ExecuteReaderAsync(token).ConfigureAwait(false);
        var found = 0;
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (!expected.TryGetValue(reader.GetGuid(0), out var shot)
                || ReadPlayer(reader, 1) != shot.PlayerId
                || !string.Equals(reader.GetString(2), shot.MapName, StringComparison.Ordinal)
                || !string.Equals(reader.GetString(3), shot.Weapon, StringComparison.Ordinal))
                throw new InvalidOperationException("Combat weapon-fire event id conflicts with a different event.");
            found++;
        }
        if (found != expected.Count) throw new InvalidOperationException("Combat weapon-fire batch verification failed.");
    }

    private static async ValueTask WriteDamageAsync(DbConnection connection, DbTransaction transaction,
        IReadOnlyList<CombatDamageEvent> hits, CancellationToken token)
    {
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            var rows = new List<string>();
            for (var index = 0; index < hits.Count; index++)
            {
                var hit = hits[index];
                var suffix = index.ToString(CultureInfo.InvariantCulture);
                rows.Add($"(@id{suffix}, @victim{suffix}, @attacker{suffix}, @occurred{suffix}, "
                    + $"@map{suffix}, @weapon{suffix}, @hitgroup{suffix}, @health{suffix}, @armor{suffix}, @team{suffix})");
                Add(insert, "@id" + suffix, hit.EventId.ToString("D"));
                Add(insert, "@victim" + suffix, hit.VictimId.SteamId64);
                Add(insert, "@attacker" + suffix, Steam(hit.AttackerId));
                Add(insert, "@occurred" + suffix, hit.OccurredAtUtc.UtcDateTime);
                Add(insert, "@map" + suffix, hit.MapName);
                Add(insert, "@weapon" + suffix, hit.Weapon);
                Add(insert, "@hitgroup" + suffix, hit.Hitgroup);
                Add(insert, "@health" + suffix, hit.DamageHealth);
                Add(insert, "@armor" + suffix, hit.DamageArmor);
                Add(insert, "@team" + suffix, hit.IsTeamDamage);
            }
            insert.CommandText = "INSERT INTO ano_combat_damage "
                + "(event_id, victim_steam_id, attacker_steam_id, occurred_at_utc, "
                + "map_name, weapon, hitgroup, damage_health, damage_armor, is_team_damage) VALUES "
                + string.Join(",", rows) + " ON DUPLICATE KEY UPDATE event_id = event_id";
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        await using var verify = connection.CreateCommand();
        verify.Transaction = transaction;
        verify.CommandText = "SELECT event_id, victim_steam_id, attacker_steam_id, "
            + "map_name, weapon, hitgroup, damage_health, damage_armor, is_team_damage "
            + "FROM ano_combat_damage WHERE event_id IN (" + AddIds(verify, hits.Select(value => value.EventId))
            + ") FOR UPDATE";
        var expected = hits.ToDictionary(value => value.EventId);
        await using var reader = await verify.ExecuteReaderAsync(token).ConfigureAwait(false);
        var found = 0;
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (!expected.TryGetValue(reader.GetGuid(0), out var hit)
                || ReadPlayer(reader, 1) != hit.VictimId || ReadPlayer(reader, 2) != hit.AttackerId
                || !string.Equals(reader.GetString(3), hit.MapName, StringComparison.Ordinal)
                || !string.Equals(reader.GetString(4), hit.Weapon, StringComparison.Ordinal)
                || reader.GetInt32(5) != hit.Hitgroup || reader.GetInt32(6) != hit.DamageHealth
                || reader.GetInt32(7) != hit.DamageArmor || reader.GetBoolean(8) != hit.IsTeamDamage)
                throw new InvalidOperationException("Combat damage event id conflicts with a different event.");
            found++;
        }
        if (found != expected.Count) throw new InvalidOperationException("Combat damage batch verification failed.");
    }

    private static string AddIds(DbCommand command, IEnumerable<Guid> ids)
    {
        var names = new List<string>();
        foreach (var id in ids)
        {
            var name = "@id" + names.Count.ToString(CultureInfo.InvariantCulture);
            names.Add(name);
            Add(command, name, id.ToString("D"));
        }
        return string.Join(",", names);
    }

    private static bool SameShot(CombatWeaponFireEvent left, CombatWeaponFireEvent right)
        => left.PlayerId == right.PlayerId && left.MapName == right.MapName && left.Weapon == right.Weapon;

    private static bool SameDamage(CombatDamageEvent left, CombatDamageEvent right)
        => left.VictimId == right.VictimId && left.AttackerId == right.AttackerId
            && left.MapName == right.MapName && left.Weapon == right.Weapon && left.Hitgroup == right.Hitgroup
            && left.DamageHealth == right.DamageHealth && left.DamageArmor == right.DamageArmor
            && left.IsTeamDamage == right.IsTeamDamage;

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
        => GetTopScoresAsync(new RankScoreWeights(killPoints, assistPoints, deathPenalty),
            limit, offset, cancellationToken);

    public ValueTask<CombatScoreRankEntry?> GetScorePlacementAsync(PlayerId playerId,
        int killPoints, int assistPoints, int deathPenalty,
        CancellationToken cancellationToken = default)
        => GetScorePlacementAsync(playerId,
            new RankScoreWeights(killPoints, assistPoints, deathPenalty), cancellationToken);

    public ValueTask<IReadOnlyList<CombatScoreRankEntry>> GetTopScoresAsync(
        RankScoreWeights weights, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(offset));
        return _database.WithConnectionAsync<IReadOnlyList<CombatScoreRankEntry>>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ScoreCte(command, weights) + """
                SELECT scored.steam_id, scored.points, profiles.last_known_name
                FROM scored
                LEFT JOIN ano_players AS profiles ON profiles.steam_id = scored.steam_id
                ORDER BY scored.points DESC, scored.steam_id ASC
                LIMIT @limit OFFSET @offset
                """;
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
        RankScoreWeights weights,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(playerId);
        return _database.WithConnectionAsync<CombatScoreRankEntry?>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ScoreCte(command, weights) + """
                , ranked AS (
                    SELECT steam_id, points,
                        ROW_NUMBER() OVER (ORDER BY points DESC, steam_id ASC) AS position
                    FROM scored
                )
                SELECT ranked.steam_id, ranked.points, ranked.position, profiles.last_known_name
                FROM ranked
                LEFT JOIN ano_players AS profiles ON profiles.steam_id = ranked.steam_id
                WHERE ranked.steam_id = @player
                """;
            Add(command, "@player", playerId.SteamId64);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
            return new CombatScoreRankEntry(
                new PlayerId(Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture)),
                reader.GetInt64(1), Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
                reader.IsDBNull(3) ? null : reader.GetString(3));
        }, cancellationToken);
    }

    public ValueTask<long> ReadRawScoreAsync(PlayerId playerId, RankScoreWeights weights,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(weights);
        return _database.WithConnectionAsync<long>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            Add(command, "@player", playerId.SteamId64);
            Add(command, "@starting_points", weights.StartingPoints);
            if (weights.Source == RankScoreSource.EventLedger)
            {
                command.CommandText = """
                    SELECT @starting_points + COALESCE((SELECT SUM(points) FROM ano_rank_point_events
                        WHERE player_steam_id = @player), 0)
                    """;
                return Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }
            Add(command, "@kill_points", weights.KillPoints);
            Add(command, "@assist_points", weights.AssistPoints);
            Add(command, "@death_penalty", weights.DeathPenalty);
            var gameplay = GameplayScore(command, weights, "@player");
            command.CommandText = $"""
                SELECT @starting_points
                + CAST((SELECT COUNT(*) FROM ano_effective_combat_kills
                    WHERE attacker_steam_id = @player) AS SIGNED) * @kill_points
                + CAST((SELECT COUNT(*) FROM ano_effective_combat_assists
                    WHERE assister_steam_id = @player) AS SIGNED) * @assist_points
                - CAST((SELECT COUNT(*) FROM ano_effective_combat_deaths
                    WHERE victim_steam_id = @player) AS SIGNED) * @death_penalty
                + {gameplay}
                """;
            return Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }, cancellationToken);
    }

    private static string GameplayScore(DbCommand command, RankScoreWeights weights, string player)
    {
        if (weights.GameplayPoints.Count == 0) return "0";
        var cases = new List<string>();
        foreach (var pair in weights.GameplayPoints.OrderBy(pair => pair.Key))
        {
            var index = cases.Count;
            cases.Add($"WHEN @kind{index} THEN @weight{index}");
            Add(command, $"@kind{index}", (byte)pair.Key);
            Add(command, $"@weight{index}", pair.Value);
        }
        return $"COALESCE((SELECT SUM(CAST(amount AS SIGNED) * CASE stat_kind "
            + string.Join(" ", cases) + $" ELSE 0 END) FROM ano_effective_gameplay_stats "
            + $"WHERE player_steam_id = {player}), 0)";
    }

    private static string ScoreCte(DbCommand command, RankScoreWeights weights)
    {
        Add(command, "@starting_points", weights.StartingPoints);
        if (weights.Source == RankScoreSource.EventLedger)
        {
            var profiles = weights.StartingPoints == 0 ? "" : "UNION SELECT steam_id FROM ano_players";
            return $"""
                WITH players AS (
                    SELECT player_steam_id AS steam_id FROM ano_rank_point_events
                    UNION SELECT player_steam_id FROM ano_rank_adjustments
                    {profiles}
                ), event_totals AS (
                    SELECT player_steam_id, SUM(points) AS points FROM ano_rank_point_events
                    GROUP BY player_steam_id
                ), scored AS (
                    SELECT players.steam_id,
                        GREATEST(0, @starting_points + COALESCE(event_totals.points, 0)
                            + COALESCE(adjustments.points, 0)) AS points
                    FROM players
                    LEFT JOIN event_totals ON event_totals.player_steam_id = players.steam_id
                    LEFT JOIN ano_rank_adjustments AS adjustments ON adjustments.player_steam_id = players.steam_id
                )
                """ + "\n";
        }
        Add(command, "@kill_points", weights.KillPoints);
        Add(command, "@assist_points", weights.AssistPoints);
        Add(command, "@death_penalty", weights.DeathPenalty);
        var gameplayScore = GameplayScore(command, weights, "players.steam_id");
        var gameplayPlayers = weights.GameplayPoints.Count == 0 ? ""
            : "UNION SELECT player_steam_id FROM ano_effective_gameplay_stats WHERE stat_kind IN ("
                + string.Join(", ", Enumerable.Range(0, weights.GameplayPoints.Count)
                    .Select(index => $"@kind{index}")) + ")";
        var profilePlayers = weights.StartingPoints == 0 ? ""
            : "UNION SELECT steam_id FROM ano_players";
        return $"""
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
                    {gameplayPlayers}
                    {profilePlayers}
                ), scored AS (
                    SELECT players.steam_id,
                        GREATEST(0,
                            @starting_points
                            + CAST((SELECT COUNT(*) FROM ano_effective_combat_kills
                                WHERE attacker_steam_id = players.steam_id) AS SIGNED)
                                * @kill_points
                            + CAST((SELECT COUNT(*) FROM ano_effective_combat_assists
                                WHERE assister_steam_id = players.steam_id) AS SIGNED) * @assist_points
                            - CAST((SELECT COUNT(*) FROM ano_effective_combat_deaths
                                WHERE victim_steam_id = players.steam_id) AS SIGNED) * @death_penalty
                            + {gameplayScore}
                            + COALESCE(adjustments.points, 0)
                        ) AS points
                    FROM players
                    LEFT JOIN ano_rank_adjustments AS adjustments
                        ON adjustments.player_steam_id = players.steam_id
                )
                """ + "\n";
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
