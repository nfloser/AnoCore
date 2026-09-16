using AnoCore.Abstractions.Persistence;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Runtime.Persistence;

public sealed class DatabaseStartupProbe
{
    private readonly IDatabase _database;
    private readonly MigrationRunner _migrations;

    public DatabaseStartupProbe(IDatabase database, IEnumerable<IDatabaseMigration> migrations)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _migrations = new MigrationRunner(database, migrations);
    }

    public async ValueTask<int> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!await _database.PingAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new DatabaseUnavailableException("AnoCore could not reach the configured database.");
        }

        return await _migrations.ApplyPendingAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class DatabaseUnavailableException : Exception
{
    public DatabaseUnavailableException(string message)
        : base(message)
    {
    }
}
