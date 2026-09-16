using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Runtime.Persistence.Migrations;

public sealed class MigrationRunner
{
    private readonly IDatabase _database;
    private readonly IDatabaseMigration[] _migrations;

    public MigrationRunner(IDatabase database, IEnumerable<IDatabaseMigration> migrations)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        ArgumentNullException.ThrowIfNull(migrations);

        _migrations = migrations.OrderBy(migration => migration.Version).ToArray();
        if (_migrations.Any(migration => migration.Version <= 0))
        {
            throw new ArgumentException("Migration versions must be positive.", nameof(migrations));
        }

        if (_migrations.Any(migration => string.IsNullOrWhiteSpace(migration.Name)))
        {
            throw new ArgumentException("Every migration must have a name.", nameof(migrations));
        }

        var duplicate = _migrations
            .GroupBy(migration => migration.Version)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Migration version {duplicate.Key} is registered more than once.",
                nameof(migrations));
        }
    }

    public async ValueTask<int> ApplyPendingAsync(CancellationToken cancellationToken = default)
    {
        await EnsureMigrationTableAsync(cancellationToken).ConfigureAwait(false);
        var applied = await ReadAppliedVersionsAsync(cancellationToken).ConfigureAwait(false);
        var count = 0;

        foreach (var migration in _migrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (applied.Contains(migration.Version))
            {
                continue;
            }

            // MariaDB/MySQL DDL can implicitly commit. Migrations therefore must be retry-safe;
            // pretending the whole schema change is transactional would provide false guarantees.
            await _database.WithConnectionAsync(async (connection, token) =>
            {
                await migration.ApplyAsync(connection, token).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO ano_schema_migrations (version, name, applied_at_utc)
                    VALUES (@version, @name, @appliedAtUtc)
                    """;
                AddParameter(command, "@version", migration.Version);
                AddParameter(command, "@name", migration.Name.Trim());
                AddParameter(command, "@appliedAtUtc", DateTime.UtcNow);
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);

            applied.Add(migration.Version);
            count++;
        }

        return count;
    }

    private ValueTask<bool> EnsureMigrationTableAsync(CancellationToken cancellationToken)
        => _database.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS ano_schema_migrations (
                    version BIGINT NOT NULL PRIMARY KEY,
                    name VARCHAR(191) NOT NULL,
                    applied_at_utc DATETIME(6) NOT NULL
                ) ENGINE=InnoDB
                """;
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    private ValueTask<HashSet<long>> ReadAppliedVersionsAsync(CancellationToken cancellationToken)
        => _database.WithConnectionAsync(async (connection, token) =>
        {
            var versions = new HashSet<long>();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT version FROM ano_schema_migrations ORDER BY version";
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                versions.Add(reader.GetInt64(0));
            }

            return versions;
        }, cancellationToken);

    internal static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
