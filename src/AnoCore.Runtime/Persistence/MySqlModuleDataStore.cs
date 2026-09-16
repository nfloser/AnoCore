using System.Text.RegularExpressions;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Persistence;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Persistence;

public sealed class MySqlModuleDataStore : IModuleDataStore
{
    private static readonly Regex ValidKey = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IDatabase _database;

    public MySqlModuleDataStore(IDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public ValueTask<string?> GetAsync(
        ModuleId module,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);
        var normalizedKey = NormalizeKey(key);
        return _database.WithConnectionAsync<string?>(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT data_json
                FROM ano_module_data
                WHERE module_id = @moduleId AND data_key = @key
                """;
            MigrationRunner.AddParameter(command, "@moduleId", module.Value);
            MigrationRunner.AddParameter(command, "@key", normalizedKey);
            var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            return value is null or DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }, cancellationToken);
    }

    public async ValueTask SetAsync(
        ModuleId module,
        string key,
        string json,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(json);
        var normalizedKey = NormalizeKey(key);
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ano_module_data (module_id, data_key, data_json, updated_at_utc)
                VALUES (@moduleId, @key, @json, @updatedAtUtc)
                ON DUPLICATE KEY UPDATE
                    data_json = VALUES(data_json),
                    updated_at_utc = VALUES(updated_at_utc)
                """;
            MigrationRunner.AddParameter(command, "@moduleId", module.Value);
            MigrationRunner.AddParameter(command, "@key", normalizedKey);
            MigrationRunner.AddParameter(command, "@json", json);
            MigrationRunner.AddParameter(command, "@updatedAtUtc", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> DeleteAsync(
        ModuleId module,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);
        var normalizedKey = NormalizeKey(key);
        return await _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM ano_module_data
                WHERE module_id = @moduleId AND data_key = @key
                """;
            MigrationRunner.AddParameter(command, "@moduleId", module.Value);
            MigrationRunner.AddParameter(command, "@key", normalizedKey);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) > 0;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !ValidKey.IsMatch(key.Trim()))
        {
            throw new ArgumentException(
                "Module data keys may contain only letters, numbers, dots, underscores and hyphens.",
                nameof(key));
        }

        return key.Trim().ToLowerInvariant();
    }
}
