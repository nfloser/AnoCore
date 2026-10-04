using AnoCore.Abstractions.Persistence;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Modules.Tournament.Persistence;

public static class TournamentPersistenceBootstrap
{
    public static async ValueTask EnsureReadyAsync(
        IDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        await new MigrationRunner(database, [new TournamentSchemaMigration012()])
            .ApplyPendingAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
