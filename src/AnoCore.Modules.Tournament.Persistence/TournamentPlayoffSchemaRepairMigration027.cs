using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Tournament.Persistence;

/// <summary>
/// Repairs deployments where version 23 was recorded but the playoff column is absent.
/// The original version-23 migration is also idempotent for fresh installations.
/// </summary>
public sealed class TournamentPlayoffSchemaRepairMigration027 : IDatabaseMigration
{
    public long Version => 27;
    public string Name => "tournament-playoff-column-repair";

    public ValueTask ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
        => new TournamentPlayoffSchemaMigration023().ApplyAsync(connection, cancellationToken);
}
