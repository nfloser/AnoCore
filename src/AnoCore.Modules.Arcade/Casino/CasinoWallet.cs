using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
namespace AnoCore.Modules.Arcade.Casino;

public sealed class CasinoRejectedException(string message) : Exception(message);

public enum CasinoAction { Load, Return, Play, Credit }
public sealed record CasinoOperation(Guid Id, PlayerId Player, CasinoAction Action, int Amount, CasinoRequest? Game);
public sealed record CasinoBalance(long Available, long Allocated, long Revision);
public sealed record CasinoReceipt(Guid Id, CasinoBalance Balance, CasinoOutcome? Outcome);
public interface ICasinoWallet
{
    ValueTask<CasinoBalance> ReadAsync(PlayerId player, CancellationToken token = default);
    ValueTask<CasinoReceipt> ApplyAsync(CasinoOperation operation, CancellationToken token = default);
    ValueTask<IReadOnlyList<CasinoReceipt>> HistoryAsync(PlayerId player, CancellationToken token = default);
}
/// <summary>Shared SteamID AP account; every transfer or game commits once under a row lock.</summary>
public sealed class MySqlCasinoWallet(IDatabase database) : ICasinoWallet
{
    public ValueTask<CasinoBalance> ReadAsync(PlayerId player, CancellationToken token = default)
        => database.WithConnectionAsync(async (connection, ct) => await BalanceAsync(connection, null, player, false, ct).ConfigureAwait(false), token);
    public ValueTask<CasinoReceipt> ApplyAsync(CasinoOperation op, CancellationToken token = default)
    {
        if (op.Id == Guid.Empty || !Enum.IsDefined(op.Action) || op.Amount is < 0 or > 1000000) throw new ArgumentException("Invalid operation.");
        if (op.Action == CasinoAction.Play) CasinoRules.Validate(op.Game ?? throw new ArgumentException("Missing game."));
        if (op.Action is CasinoAction.Credit or CasinoAction.Load && op.Amount == 0) throw new ArgumentException("Amount required.");
        var request = JsonSerializer.Serialize(new { player = op.Player.SteamId64, action = op.Action, amount = op.Amount, game = op.Game, rules = CasinoRules.Version });
        return database.InTransactionAsync(async (connection, transaction, ct) =>
        {
            await ExecuteAsync(connection, transaction, "INSERT IGNORE INTO ano_ap_accounts VALUES (@player, 0, 0, 0)", op.Player, ct).ConfigureAwait(false);
            var before = await BalanceAsync(connection, transaction, op.Player, true, ct).ConfigureAwait(false);
            await using (var replay = Command(connection, transaction, "SELECT request_json, result_json FROM ano_casino_operations WHERE operation_id=@id", op.Player))
            {
                Add(replay, "@id", op.Id.ToString("N"));
                await using var reader = await replay.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    if (reader.GetString(0) != request) throw new CasinoRejectedException("Operation ID reused with different request.");
                    return JsonSerializer.Deserialize<CasinoReceipt>(reader.GetString(1))!;
                }
            }
            var available = before.Available; var allocated = before.Allocated; CasinoOutcome? outcome = null;
            switch (op.Action)
            {
                case CasinoAction.Load:
                    if (available < op.Amount) throw new CasinoRejectedException("Nicht genügend AP im Profilkonto.");
                    available -= op.Amount; allocated = checked(allocated + op.Amount); break;
                case CasinoAction.Return: available = checked(available + allocated); allocated = 0; break;
                case CasinoAction.Credit: available = checked(available + op.Amount); break;
                case CasinoAction.Play:
                    if (allocated < op.Game!.Stake) throw new CasinoRejectedException("Nicht genügend Automatenguthaben.");
                    outcome = CasinoRules.Draw(op.Game);
                    allocated = checked(allocated - op.Game.Stake + outcome.Payout); break;
            }
            if (available + allocated > 1000000000L) throw new CasinoRejectedException("AP-Kontolimit erreicht.");
            var result = new CasinoReceipt(op.Id, new(available, allocated, checked(before.Revision + 1)), outcome);
            await using (var update = Command(connection, transaction, "UPDATE ano_ap_accounts SET available=@available, allocated=@allocated, revision=@revision WHERE player_steam_id=@player", op.Player))
            {
                Add(update, "@available", available); Add(update, "@allocated", allocated); Add(update, "@revision", result.Balance.Revision);
                await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await using (var insert = Command(connection, transaction, "INSERT INTO ano_casino_operations VALUES (@id,@player,@request,@result,UTC_TIMESTAMP(6))", op.Player))
            {
                Add(insert, "@id", op.Id.ToString("N")); Add(insert, "@request", request); Add(insert, "@result", JsonSerializer.Serialize(result));
                await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            return result;
        }, cancellationToken: token);
    }
    public ValueTask<IReadOnlyList<CasinoReceipt>> HistoryAsync(PlayerId player, CancellationToken token = default)
        => database.WithConnectionAsync<IReadOnlyList<CasinoReceipt>>(async (connection, ct) =>
        {
            await using var cmd = Command(connection, null, "SELECT result_json FROM ano_casino_operations WHERE player_steam_id=@player ORDER BY created_at_utc DESC, operation_id DESC LIMIT 20", player);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var result = new List<CasinoReceipt>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) result.Add(JsonSerializer.Deserialize<CasinoReceipt>(reader.GetString(0))!);
            return result.AsReadOnly();
        }, token);
    private static async ValueTask<CasinoBalance> BalanceAsync(DbConnection connection, DbTransaction? transaction, PlayerId player, bool locked, CancellationToken token)
    {
        await using var cmd = Command(connection, transaction, "SELECT available,allocated,revision FROM ano_ap_accounts WHERE player_steam_id=@player" + (locked ? " FOR UPDATE" : ""), player);
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)) : new(0, 0, 0);
    }
    private static async ValueTask ExecuteAsync(DbConnection c, DbTransaction t, string sql, PlayerId player, CancellationToken token)
    { await using var cmd = Command(c, t, sql, player); await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
    private static DbCommand Command(DbConnection c, DbTransaction? t, string sql, PlayerId player)
    { var cmd = c.CreateCommand(); cmd.Transaction = t; cmd.CommandText = sql; Add(cmd, "@player", player.SteamId64.ToString(CultureInfo.InvariantCulture)); return cmd; }
    private static void Add(DbCommand c, string name, object value)
    { var p = c.CreateParameter(); p.ParameterName = name; p.Value = value; c.Parameters.Add(p); }
}
