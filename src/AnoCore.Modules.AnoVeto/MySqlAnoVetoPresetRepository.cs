using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.AnoVeto;

/// <summary>
/// Durable, revisioned, owner-scoped personal veto preset repository.
/// Every mutation locks the owner's row, including first-time creation.
/// </summary>
public sealed class MySqlAnoVetoPresetRepository : IAnoVetoPresetRepository
{
    private readonly IDatabase _database;

    public MySqlAnoVetoPresetRepository(IDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public ValueTask<IReadOnlyList<(AnoVetoPreset Preset, long Revision)>> ListAsync(
        ulong ownerSteamId64, CancellationToken cancellationToken = default)
    {
        ValidateOwner(ownerSteamId64);
        return _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT display_name, map_ids_json, revision
                FROM ano_veto_presets
                WHERE owner_steamid = @owner ORDER BY display_name ASC
                """;
            Add(cmd, "@owner", Owner(ownerSteamId64));
            var results = new List<(AnoVetoPreset Preset, long Revision)>();
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                results.Add((Deserialize(ownerSteamId64, reader.GetString(0), reader.GetString(1)), reader.GetInt64(2)));
            return (IReadOnlyList<(AnoVetoPreset Preset, long Revision)>)results;
        }, cancellationToken);
    }

    public ValueTask<(AnoVetoPreset Preset, long Revision)?> GetAsync(
        ulong ownerSteamId64, string name, CancellationToken cancellationToken = default)
    {
        ValidateOwner(ownerSteamId64);
        ValidateName(name);
        return _database.WithConnectionAsync<(AnoVetoPreset Preset, long Revision)?>(async (connection, token) =>
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT display_name, map_ids_json, revision
                FROM ano_veto_presets WHERE owner_steamid = @owner AND name_key = @name
                """;
            Add(cmd, "@owner", Owner(ownerSteamId64));
            Add(cmd, "@name", Key(name));
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
            return (Deserialize(ownerSteamId64, reader.GetString(0), reader.GetString(1)), reader.GetInt64(2));
        }, cancellationToken);
    }

    public ValueTask<long> CreateAsync(AnoVetoPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return _database.InTransactionAsync(async (connection, tx, token) =>
        {
            await LockOwnerAsync(connection, tx, preset.OwnerSteamId64, token).ConfigureAwait(false);
            await using (var count = connection.CreateCommand())
            {
                count.Transaction = tx;
                count.CommandText = "SELECT COUNT(*) FROM ano_veto_presets WHERE owner_steamid = @owner";
                Add(count, "@owner", Owner(preset.OwnerSteamId64));
                var existing = Convert.ToInt64(await count.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (existing >= AnoVetoPresetCatalog.MaxPresetsPerOwner)
                    throw new InvalidOperationException("Preset limit reached.");
            }
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO ano_veto_presets (owner_steamid, name_key, display_name, map_ids_json, revision, updated_at_utc)
                VALUES (@owner, @key, @name, @maps, 1, UTC_TIMESTAMP(6))
                """;
            BindPreset(cmd, preset);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return 1L;
        }, cancellationToken: cancellationToken);
    }

    public ValueTask<long> ReplaceAsync(
        ulong ownerSteamId64, string existingName, long expectedRevision,
        AnoVetoPreset replacement, CancellationToken cancellationToken = default)
    {
        ValidateOwner(ownerSteamId64);
        ValidateName(existingName);
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.OwnerSteamId64 != ownerSteamId64)
            throw new UnauthorizedAccessException("Preset ownership cannot be changed.");
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));

        return _database.InTransactionAsync(async (connection, tx, token) =>
        {
            await LockOwnerAsync(connection, tx, ownerSteamId64, token).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE ano_veto_presets SET name_key = @newKey, display_name = @name,
                    map_ids_json = @maps, revision = revision + 1, updated_at_utc = UTC_TIMESTAMP(6)
                WHERE owner_steamid = @owner AND name_key = @oldKey AND revision = @revision
                """;
            Add(cmd, "@owner", Owner(ownerSteamId64));
            Add(cmd, "@oldKey", Key(existingName));
            Add(cmd, "@newKey", Key(replacement.Name));
            Add(cmd, "@name", replacement.Name);
            Add(cmd, "@maps", JsonSerializer.Serialize(replacement.MapIds));
            Add(cmd, "@revision", expectedRevision);
            if (await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Preset revision conflict.");
            return checked(expectedRevision + 1);
        }, cancellationToken: cancellationToken);
    }

    public async ValueTask DeleteAsync(
        ulong ownerSteamId64, string name, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateOwner(ownerSteamId64);
        ValidateName(name);
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        await _database.InTransactionAsync(async (connection, tx, token) =>
        {
            await LockOwnerAsync(connection, tx, ownerSteamId64, token).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                DELETE FROM ano_veto_presets
                WHERE owner_steamid = @owner AND name_key = @key AND revision = @revision
                """;
            Add(cmd, "@owner", Owner(ownerSteamId64));
            Add(cmd, "@key", Key(name));
            Add(cmd, "@revision", expectedRevision);
            if (await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Preset revision conflict.");
            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask LockOwnerAsync(
        DbConnection connection, DbTransaction tx, ulong owner, CancellationToken token)
    {
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = "INSERT IGNORE INTO ano_veto_preset_owners (owner_steamid) VALUES (@owner)";
            Add(insert, "@owner", Owner(owner));
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        await using var read = connection.CreateCommand();
        read.Transaction = tx;
        read.CommandText = "SELECT owner_steamid FROM ano_veto_preset_owners WHERE owner_steamid = @owner FOR UPDATE";
        Add(read, "@owner", Owner(owner));
        if (await read.ExecuteScalarAsync(token).ConfigureAwait(false) is null)
            throw new InvalidOperationException("Could not lock preset owner.");
    }

    private static AnoVetoPreset Deserialize(ulong owner, string name, string json)
    {
        var ids = JsonSerializer.Deserialize<string[]>(json)
            ?? throw new InvalidDataException("Saved veto preset has no maps.");
        return new AnoVetoPreset(owner, name, ids);
    }

    private static void BindPreset(DbCommand cmd, AnoVetoPreset preset)
    {
        Add(cmd, "@owner", Owner(preset.OwnerSteamId64));
        Add(cmd, "@key", Key(preset.Name));
        Add(cmd, "@name", preset.Name);
        Add(cmd, "@maps", JsonSerializer.Serialize(preset.MapIds));
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void ValidateOwner(ulong owner)
    {
        if (owner == 0) throw new ArgumentOutOfRangeException(nameof(owner));
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > AnoVetoPreset.MaxNameLength)
            throw new ArgumentException("Invalid preset name.", nameof(name));
    }

    private static string Owner(ulong value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Key(string value) => value.Trim().ToUpperInvariant();
}
