using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class ProgressionSeasonSchemaMigration014 : IDatabaseMigration
{
    public long Version => 14;
    public string Name => "progression-season-lifecycle";

    public ValueTask ApplyAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
