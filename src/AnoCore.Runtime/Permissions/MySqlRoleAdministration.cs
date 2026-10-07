using System.Data.Common;
using System.Text.Json;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Permissions;

/// <summary>Authorization policy and administrative audit share one locked transaction.</summary>
public sealed class MySqlRoleAdministration(IDatabase database, AuthorizationService authorization, TimeProvider? clock = null, Func<Guid>? newAuditId = null)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<Guid> _newAuditId = newAuditId ?? Guid.NewGuid;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public ValueTask ChangeAsync(PlayerId? actor, PlayerId target, RoleId role, int minutes, bool revoke,
        string reason, CancellationToken cancellationToken = default)
    {
        if (minutes is < 0 or > 5256000) throw new ArgumentOutOfRangeException(nameof(minutes));
        return MutateAsync(actor, target, "roles." + (revoke ? "revoke" : "grant"),
            $"role={role.Value}; minutes={minutes}; {reason}",
            state =>
            {
                var now = _clock.GetUtcNow();
                return RoleAdministrationPolicy.ChangeAsync(state, actor, target, role,
                    minutes == 0 ? null : now.AddMinutes(minutes), revoke, now);
            }, cancellationToken);
    }

    public ValueTask DefineAsync(PlayerId? actor, AuthorizationRole role, CancellationToken cancellationToken = default)
    {
        if (actor is not null) throw new UnauthorizedAccessException("Role definitions are console-only.");
        return MutateAsync(null, null, "roles.define", "role=" + role.Id.Value,
            state => ValueTask.FromResult(new AuthorizationState(state.Roles.Where(value => value.Id != role.Id)
                .Append(role).ToArray(), state.Players)), cancellationToken);
    }

    private async ValueTask MutateAsync(PlayerId? actor, PlayerId? target, string action, string reason,
        Func<AuthorizationState, ValueTask<AuthorizationState>> mutate, CancellationToken cancellationToken)
    {
        if (reason.Length > 512 || reason.Any(char.IsControl)) throw new ArgumentException("Invalid bounded audit reason.", nameof(reason));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await database.InTransactionAsync(async (connection, transaction, token) =>
            {
                await Execute(connection, transaction, """
                    INSERT INTO ano_module_data (module_id, data_key, data_json, updated_at_utc)
                    VALUES ('authorization', 'state', @empty, @now)
                    ON DUPLICATE KEY UPDATE data_key = data_key
                    """, token, ("@empty", JsonSerializer.Serialize(AuthorizationState.Empty)), ("@now", _clock.GetUtcNow().UtcDateTime));
                await using var read = connection.CreateCommand();
                read.Transaction = transaction;
                read.CommandText = "SELECT data_json FROM ano_module_data WHERE module_id = 'authorization' AND data_key = 'state' FOR UPDATE";
                var text = (string)(await read.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                var state = JsonSerializer.Deserialize<AuthorizationState>(text, Json) ?? throw new InvalidDataException("Invalid authorization policy.");
                var replacement = await mutate(state).ConfigureAwait(false);
                await Execute(connection, transaction, "UPDATE ano_module_data SET data_json = @json, updated_at_utc = @now WHERE module_id = 'authorization' AND data_key = 'state'",
                    token, ("@json", JsonSerializer.Serialize(replacement, Json)), ("@now", _clock.GetUtcNow().UtcDateTime));
                await Execute(connection, transaction, """
                    INSERT INTO ano_admin_action_audit (audit_id, action_id, actor_steam_id, target_steam_id, reason, occurred_at_utc)
                    VALUES (@id, @action, @actor, @target, @reason, @now)
                    """, token, ("@id", _newAuditId().ToString("D")), ("@action", action),
                    ("@actor", actor is null ? DBNull.Value : actor.SteamId64), ("@target", target is null ? DBNull.Value : target.SteamId64),
                    ("@reason", reason), ("@now", _clock.GetUtcNow().UtcDateTime));
                return true;
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            // Do not let caller cancellation leave a committed policy unapplied in this host.
            try { await authorization.ReloadAsync(CancellationToken.None).ConfigureAwait(false); }
            catch
            {
                authorization.FailClosed();
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private static async Task Execute(DbConnection connection, DbTransaction transaction, string sql,
        CancellationToken token, params (string Name, object Value)[] values)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in values) MigrationRunner.AddParameter(command, name, value);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
