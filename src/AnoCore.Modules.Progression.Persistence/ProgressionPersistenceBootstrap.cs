using AnoCore.Abstractions.Persistence;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Modules.Progression.Persistence;

public static class ProgressionPersistenceBootstrap
{
    public static async ValueTask EnsureReadyAsync(
        IDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        await new MigrationRunner(database,
            [new ProgressionSchemaMigration013(), new ProgressionSeasonSchemaMigration014(), new ProgressionSeasonXpSchemaMigration015()])
            .ApplyPendingAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
