using System.Data;
using System.Data.Common;
using System.Globalization;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class MySqlGameplayXpRepository : IGameplayXpRepository
{
    private readonly IDatabase _database;
    public MySqlGameplayXpRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<IReadOnlyList<ProgressionGrantRecord>> ReconcileAsync(PlayerId playerId, GameplayXpPolicy policy,
        ProgressionDefinitionSnapshot definitions, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(definitions);
        cancellationToken.ThrowIfCancellationRequested();
        var utc = at.ToUniversalTime();
        at = new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
        if (at < policy.EarnFromUtc || policy.KillXp == 0 && policy.AssistXp == 0 && policy.GameplayXp.Count == 0)
            return ValueTask.FromResult<IReadOnlyList<ProgressionGrantRecord>>([]);

        return _database.InTransactionAsync<IReadOnlyList<ProgressionGrantRecord>>(async (connection, transaction, token) =>
        {
            await MySqlProgressionGrantRepository.EnsureAccountAsync(connection, transaction, playerId, token).ConfigureAwait(false);
            _ = await MySqlProgressionGrantRepository.ReadLifetimeForUpdateAsync(connection, transaction, playerId, token).ConfigureAwait(false);
            var events = await ReadPendingAsync(connection, transaction, playerId, policy, at, token).ConfigureAwait(false);
            var grants = new List<ProgressionGrantRecord>();
            foreach (var item in events)
            {
                token.ThrowIfCancellationRequested();
                var weight = item.Kind switch
                {
                    -1 => policy.KillXp,
                    -2 => policy.AssistXp,
                    _ => policy.GameplayXp[(GameplayStatKind)item.Kind],
                };
                var reason = item.Kind switch
                {
                    -1 => "gameplay.kill",
                    -2 => "gameplay.assist",
                    _ => "gameplay." + ((GameplayStatKind)item.Kind).ToString(),
                };
                var baseXp = checked((long)weight * item.Amount);
                var boost = policy.ResolveGameplayBoost(definitions, item.At);
                var candidate = new ProgressionGrantCandidate(item.GrantId, ProgressionXpSource.Gameplay, baseXp,
                    checked((long)decimal.Truncate(baseXp * boost.Multiplier)), reason, item.At,
                    boost.BoostId, boost.Multiplier);
                var result = await MySqlProgressionGrantRepository.ApplyInTransactionAsync(
                    connection, transaction, playerId, candidate, token).ConfigureAwait(false);
                if (!result.Applied)
                    throw new InvalidOperationException("Pending gameplay XP event already has a committed grant.");
                grants.Add(result.Grant);
            }
            return grants.AsReadOnly();
        }, IsolationLevel.ReadCommitted, cancellationToken);
    }

    private static async ValueTask<IReadOnlyList<PendingEvent>> ReadPendingAsync(DbConnection connection,
        DbTransaction transaction, PlayerId playerId, GameplayXpPolicy policy, DateTimeOffset at, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var sources = new List<string>();
        if (policy.KillXp > 0)
            sources.Add("""
                SELECT CONCAT('gameplay:kill:', LOWER(event_id)) AS grant_id, -1 AS kind, 1 AS amount, occurred_at_utc
                FROM ano_combat_deaths WHERE attacker_steam_id = @player AND is_team_kill = 0
                    AND attacker_steam_id <> victim_steam_id AND occurred_at_utc >= @start AND occurred_at_utc <= @at
                """);
        if (policy.AssistXp > 0)
            sources.Add("""
                SELECT CONCAT('gameplay:assist:', LOWER(event_id)) AS grant_id, -2 AS kind, 1 AS amount, occurred_at_utc
                FROM ano_combat_deaths WHERE assister_steam_id = @player AND is_team_kill = 0
                    AND attacker_steam_id IS NOT NULL AND assister_steam_id <> attacker_steam_id
                    AND assister_steam_id <> victim_steam_id AND occurred_at_utc >= @start AND occurred_at_utc <= @at
                """);
        if (policy.GameplayXp.Count > 0)
        {
            var kinds = policy.GameplayXp.Keys.OrderBy(kind => kind).ToArray();
            var parameters = new List<string>();
            for (var index = 0; index < kinds.Length; index++)
            {
                var name = "@kind" + index.ToString(CultureInfo.InvariantCulture);
                parameters.Add(name);
                Add(command, name, (byte)kinds[index]);
            }
            sources.Add("""
                SELECT CONCAT('gameplay:stat:', LOWER(event_id)) AS grant_id, stat_kind AS kind, amount, occurred_at_utc
                FROM ano_gameplay_stats WHERE player_steam_id = @player AND occurred_at_utc >= @start
                    AND occurred_at_utc <= @at AND stat_kind IN (
                """ + string.Join(",", parameters) + ")");
        }
        command.CommandText = """
            SELECT pending.grant_id, pending.kind, pending.amount, pending.occurred_at_utc FROM (
            """ + string.Join(" UNION ALL ", sources) + """
            ) AS pending LEFT JOIN ano_progression_grants AS committed
                ON committed.player_steam_id = @player AND committed.grant_id = pending.grant_id
            WHERE committed.grant_id IS NULL
            ORDER BY pending.occurred_at_utc, pending.grant_id
            LIMIT @limit
            """;
        Add(command, "@player", playerId.SteamId64);
        Add(command, "@start", policy.EarnFromUtc.UtcDateTime);
        Add(command, "@at", at.UtcDateTime);
        Add(command, "@limit", policy.BatchSize);
        var result = new List<PendingEvent>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var amount = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture);
            if (amount <= 0) throw new InvalidOperationException("Stored gameplay XP event amount must be positive.");
            result.Add(new PendingEvent(reader.GetString(0), Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                amount, new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc))));
        }
        return result;
    }

    private sealed record PendingEvent(string GrantId, int Kind, int Amount, DateTimeOffset At);

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
