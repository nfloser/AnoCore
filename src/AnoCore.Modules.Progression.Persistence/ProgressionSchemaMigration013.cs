using System.Data.Common;
using AnoCore.Abstractions.Persistence;

namespace AnoCore.Modules.Progression.Persistence;

public sealed class ProgressionSchemaMigration013 : IDatabaseMigration
{
    public long Version => 13;

    public string Name => "progression-lifetime-xp";

    public ValueTask ApplyAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
