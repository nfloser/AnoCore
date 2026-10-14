using AnoCore.Abstractions.Persistence;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Modules.AnoVeto;

public static class AnoVetoPresetPersistenceBootstrap
{
    public static async ValueTask EnsureReadyAsync(
        IDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        await new MigrationRunner(database, [new AnoVetoPresetSchemaMigration029()])
            .ApplyPendingAsync(cancellationToken).ConfigureAwait(false);
    }
}
