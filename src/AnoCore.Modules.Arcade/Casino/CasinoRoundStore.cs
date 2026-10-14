using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Arcade.Casino;

public enum CasinoRoundKind { Blackjack, Risk, BonusSlot }
public enum CasinoRoundStatus { Active, Completed }

public sealed record CasinoRoundSnapshot(
    Guid RoundId,
    PlayerId Player,
    CasinoRoundKind Kind,
    string RulesVersion,
    string StateJson,
    CasinoRoundStatus Status,
    long Revision,
    long ReservedPayout);

public sealed record CasinoRoundOperation(
    Guid Id,
    Guid RoundId,
    PlayerId Player,
    CasinoRoundKind Kind,
    long ExpectedRevision,
    string RequestJson);

/// <summary>Money required and maximum gross payout exposure, calculated without drawing.</summary>
public sealed record CasinoRoundPlan(long AdditionalDebit, long MaximumPayoutExposure);

/// <summary>
/// A transition calculated under the account and round locks. Intermediate winnings remain in
/// StateJson and ReservedPayout; Payout is credited exactly once only on a terminal transition.
/// </summary>
public sealed record CasinoRoundMutation(
    string RulesVersion,
    string StateJson,
    long AdditionalDebit,
    long Payout,
    long ReservedPayout,
    bool Completed,
    string OutcomeJson);

public sealed record CasinoRoundReceipt(
    Guid OperationId,
    CasinoRoundSnapshot Round,
    CasinoBalance Balance,
    string OutcomeJson);

public interface ICasinoRoundStore
{
    ValueTask<CasinoRoundSnapshot?> ReadActiveAsync(PlayerId player, CancellationToken token = default);
    ValueTask<CasinoRoundReceipt> ApplyAsync(
        CasinoRoundOperation operation,
        Func<CasinoRoundSnapshot?, CasinoRoundPlan> plan,
        Func<CasinoRoundSnapshot?, CasinoRoundMutation> transition,
        CancellationToken token = default);
}

/// <summary>
/// Serializes all money and round changes account-first, then round. The transition callback is
/// invoked only after idempotent replay and expected-revision checks, so authoritative randomness
/// is never consumed by a duplicate or stale request.
/// </summary>
public sealed class MySqlCasinoRoundStore(IDatabase database) : ICasinoRoundStore
{
    private const long AccountLimit = 1_000_000_000;
    private const int MaximumJsonLength = 262_144;

    public ValueTask<CasinoRoundSnapshot?> ReadActiveAsync(PlayerId player, CancellationToken token = default) =>
        database.WithConnectionAsync(async (connection, ct) =>
        {
            await using var command = Command(connection, null, """
                SELECT round_id,game_kind,rules_version,state_json,status,revision,reserved_payout
                FROM ano_casino_rounds WHERE active_player_steam_id=@player
                """, player);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadRound(reader, player) : null;
        }, token);

    public ValueTask<CasinoRoundReceipt> ApplyAsync(
        CasinoRoundOperation operation,
        Func<CasinoRoundSnapshot?, CasinoRoundPlan> plan,
        Func<CasinoRoundSnapshot?, CasinoRoundMutation> transition,
        CancellationToken token = default)
    {
        Validate(operation, plan, transition);
        var canonicalRequest = JsonSerializer.Serialize(new
        {
            player = operation.Player.SteamId64,
            round = operation.RoundId,
            kind = operation.Kind,
            expectedRevision = operation.ExpectedRevision,
            request = operation.RequestJson,
        });

        return database.InTransactionAsync(async (connection, transaction, ct) =>
        {
            await ExecuteAsync(connection, transaction,
                "INSERT INTO ano_ap_accounts VALUES (@player,0,0,0) ON DUPLICATE KEY UPDATE player_steam_id=VALUES(player_steam_id)",
                operation.Player, ct).ConfigureAwait(false);
            var balance = await ReadBalanceAsync(connection, transaction, operation.Player, ct).ConfigureAwait(false);

            await using (var replay = Command(connection, transaction, """
                SELECT request_json,result_json,player_steam_id,round_id
                FROM ano_casino_round_actions WHERE operation_id=@operation
                """, operation.Player))
            {
                Add(replay, "@operation", operation.Id.ToString("N"));
                await using var reader = await replay.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    if (reader.GetString(0) != canonicalRequest
                        || Convert.ToUInt64(reader.GetValue(2), CultureInfo.InvariantCulture) != operation.Player.SteamId64
                        || reader.GetString(3) != operation.RoundId.ToString("N"))
                        throw new CasinoRejectedException("Operation ID reused with a different round request.");
                    return JsonSerializer.Deserialize<CasinoRoundReceipt>(reader.GetString(1))
                        ?? throw new InvalidOperationException("Stored casino round receipt is invalid.");
                }
            }

            var current = await ReadRoundForUpdateAsync(connection, transaction, operation, ct).ConfigureAwait(false);
            if (operation.ExpectedRevision == 0)
            {
                if (current is not null) throw new CasinoRejectedException("Round ID already exists.");
                var active = await ReadActiveForUpdateAsync(connection, transaction, operation.Player, ct).ConfigureAwait(false);
                if (active is not null) throw new CasinoRejectedException("Finish the active casino round first.");
            }
            else
            {
                if (current is null || current.Status != CasinoRoundStatus.Active)
                    throw new CasinoRejectedException("Active casino round was not found.");
                if (current.Player != operation.Player || current.Kind != operation.Kind)
                    throw new CasinoRejectedException("Casino round ownership or game type does not match.");
                if (current.Revision != operation.ExpectedRevision)
                    throw new CasinoRejectedException("Casino round changed; reload it before retrying.");
            }

            var planned = plan(current);
            Validate(planned);
            if (balance.Allocated < planned.AdditionalDebit)
                throw new CasinoRejectedException("Nicht genügend Automatenguthaben.");
            if (balance.Available + balance.Allocated - planned.AdditionalDebit + planned.MaximumPayoutExposure > AccountLimit)
                throw new CasinoRejectedException("Kontolimit: nicht genügend Spielraum für die maximale Rundenauszahlung.");

            // Authoritative randomness may be consumed only after the bounded plan was admitted.
            var mutation = transition(current);
            Validate(mutation);
            if (mutation.AdditionalDebit != planned.AdditionalDebit
                || mutation.Payout + mutation.ReservedPayout > planned.MaximumPayoutExposure)
                throw new InvalidOperationException("Casino round transition exceeded its admitted monetary plan.");
            if (current is not null && !string.Equals(current.RulesVersion, mutation.RulesVersion, StringComparison.Ordinal))
                throw new CasinoRejectedException("Casino round rules version cannot change during a round.");
            var resultingRevision = checked(operation.ExpectedRevision + 1);
            var available = balance.Available;
            var allocated = balance.Allocated;
            if (allocated < mutation.AdditionalDebit)
                throw new CasinoRejectedException("Nicht genügend Automatenguthaben.");
            allocated = checked(allocated - mutation.AdditionalDebit + mutation.Payout);
            if (available + allocated + mutation.ReservedPayout > AccountLimit)
                throw new CasinoRejectedException("Kontolimit: nicht genügend Spielraum für die maximale Rundenauszahlung.");

            var status = mutation.Completed ? CasinoRoundStatus.Completed : CasinoRoundStatus.Active;
            var round = new CasinoRoundSnapshot(operation.RoundId, operation.Player, operation.Kind,
                mutation.RulesVersion, mutation.StateJson, status, resultingRevision, mutation.ReservedPayout);
            var nextBalance = new CasinoBalance(available, allocated, checked(balance.Revision + 1));
            var receipt = new CasinoRoundReceipt(operation.Id, round, nextBalance, mutation.OutcomeJson);

            await WriteRoundAsync(connection, transaction, round, current is null, ct).ConfigureAwait(false);
            await using (var account = Command(connection, transaction, """
                UPDATE ano_ap_accounts SET available=@available,allocated=@allocated,revision=@revision
                WHERE player_steam_id=@player
                """, operation.Player))
            {
                Add(account, "@available", available);
                Add(account, "@allocated", allocated);
                Add(account, "@revision", nextBalance.Revision);
                await account.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await using (var action = Command(connection, transaction, """
                INSERT INTO ano_casino_round_actions
                    (operation_id,round_id,player_steam_id,expected_revision,request_json,result_json,resulting_revision,created_at_utc)
                VALUES (@operation,@round,@player,@expected,@request,@result,@resulting,UTC_TIMESTAMP(6))
                """, operation.Player))
            {
                Add(action, "@operation", operation.Id.ToString("N"));
                Add(action, "@round", operation.RoundId.ToString("N"));
                Add(action, "@expected", operation.ExpectedRevision);
                Add(action, "@request", canonicalRequest);
                Add(action, "@result", JsonSerializer.Serialize(receipt));
                Add(action, "@resulting", resultingRevision);
                await action.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            return receipt;
        }, cancellationToken: token);
    }

    private static void Validate(CasinoRoundOperation operation, Delegate plan, Delegate transition)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(transition);
        if (operation.Id == Guid.Empty || operation.RoundId == Guid.Empty || !Enum.IsDefined(operation.Kind)
            || operation.ExpectedRevision < 0 || string.IsNullOrWhiteSpace(operation.RequestJson)
            || operation.RequestJson.Length > MaximumJsonLength)
            throw new ArgumentException("Invalid casino round operation.", nameof(operation));
    }

    private static void Validate(CasinoRoundPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.AdditionalDebit is < 0 or > 1_000_000
            || plan.MaximumPayoutExposure is < 0 or > AccountLimit)
            throw new ArgumentException("Invalid casino round plan.", nameof(plan));
    }

    private static void Validate(CasinoRoundMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (string.IsNullOrWhiteSpace(mutation.RulesVersion) || mutation.RulesVersion.Length > 64
            || string.IsNullOrWhiteSpace(mutation.StateJson) || mutation.StateJson.Length > MaximumJsonLength
            || string.IsNullOrWhiteSpace(mutation.OutcomeJson) || mutation.OutcomeJson.Length > MaximumJsonLength
            || mutation.AdditionalDebit < 0 || mutation.Payout < 0 || mutation.ReservedPayout < 0
            || mutation.AdditionalDebit > 1_000_000 || mutation.Payout > AccountLimit || mutation.ReservedPayout > AccountLimit
            || mutation.Completed && mutation.ReservedPayout != 0
            )
            throw new ArgumentException("Invalid casino round mutation.", nameof(mutation));
    }

    private static async ValueTask<CasinoBalance> ReadBalanceAsync(
        DbConnection connection, DbTransaction transaction, PlayerId player, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT available,allocated,revision FROM ano_ap_accounts WHERE player_steam_id=@player FOR UPDATE", player);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("Casino account was not created.");
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static async ValueTask<CasinoRoundSnapshot?> ReadRoundForUpdateAsync(
        DbConnection connection, DbTransaction transaction, CasinoRoundOperation operation, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT round_id,game_kind,rules_version,state_json,status,revision,reserved_payout
            FROM ano_casino_rounds WHERE round_id=@round AND player_steam_id=@player FOR UPDATE
            """, operation.Player);
        Add(command, "@round", operation.RoundId.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadRound(reader, operation.Player) : null;
    }

    private static async ValueTask<CasinoRoundSnapshot?> ReadActiveForUpdateAsync(
        DbConnection connection, DbTransaction transaction, PlayerId player, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT round_id,game_kind,rules_version,state_json,status,revision,reserved_payout
            FROM ano_casino_rounds WHERE active_player_steam_id=@player FOR UPDATE
            """, player);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadRound(reader, player) : null;
    }

    private static CasinoRoundSnapshot ReadRound(DbDataReader reader, PlayerId player) => new(
        Guid.ParseExact(reader.GetString(0), "N"), player, (CasinoRoundKind)reader.GetByte(1), reader.GetString(2),
        reader.GetString(3), Enum.Parse<CasinoRoundStatus>(reader.GetString(4), true), reader.GetInt64(5), reader.GetInt64(6));

    private static async ValueTask WriteRoundAsync(
        DbConnection connection, DbTransaction transaction, CasinoRoundSnapshot round, bool insert, CancellationToken token)
    {
        await using var command = Command(connection, transaction, insert ? """
            INSERT INTO ano_casino_rounds
                (round_id,player_steam_id,game_kind,rules_version,state_json,status,revision,reserved_payout,created_at_utc,updated_at_utc)
            VALUES (@round,@player,@kind,@rules,@state,@status,@revision,@reserve,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6))
            """ : """
            UPDATE ano_casino_rounds SET rules_version=@rules,state_json=@state,status=@status,
                revision=@revision,reserved_payout=@reserve,updated_at_utc=UTC_TIMESTAMP(6)
            WHERE round_id=@round AND player_steam_id=@player
            """, round.Player);
        Add(command, "@round", round.RoundId.ToString("N"));
        Add(command, "@kind", (byte)round.Kind);
        Add(command, "@rules", round.RulesVersion);
        Add(command, "@state", round.StateJson);
        Add(command, "@status", round.Status.ToString().ToLowerInvariant());
        Add(command, "@revision", round.Revision);
        Add(command, "@reserve", round.ReservedPayout);
        var changed = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        if (changed != 1) throw new InvalidOperationException("Casino round write did not affect exactly one row.");
    }

    private static async ValueTask ExecuteAsync(
        DbConnection connection, DbTransaction transaction, string sql, PlayerId player, CancellationToken token)
    {
        await using var command = Command(connection, transaction, sql, player);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql, PlayerId player)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        Add(command, "@player", player.SteamId64.ToString(CultureInfo.InvariantCulture));
        return command;
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
