using AnoCore.Modules.Admin;
using AnoCore.Runtime.Persistence;
using AnoCore.Runtime.Persistence.Migrations;

namespace AnoCore.Tests.Admin;

[TestClass]
[DoNotParallelize]
public sealed class ModerationWebhookFeedTests
{
    [TestMethod]
    public async Task FeedCannotReadUncommittedActions()
    {
        var connection = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("MariaDB not configured.");
        var database = new MySqlDatabase(connection!);
        await new MigrationRunner(database, [new CoreSchemaMigration001(), new ModerationSchemaMigration002(), new AdminAuditSchemaMigration003()]).ApplyPendingAsync();
        var id = Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow;
        try
        {
            await database.InTransactionAsync(async (db, transaction, token) =>
            {
                await using var insert = db.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO ano_admin_action_audit (audit_id, action_id, reason, occurred_at_utc) VALUES (@id, 'admin.kick', 'test', @now)";
                AddParameter(insert, "@id", id);
                AddParameter(insert, "@now", now.UtcDateTime);
                await insert.ExecuteNonQueryAsync(token);
                Assert.IsFalse((await ModerationWebhookPump.ReadCommittedAsync(database, now.AddSeconds(-1), 64, token))
                    .Any(item => item.Id == "admin:" + id));
                return true;
            });
            Assert.IsTrue((await ModerationWebhookPump.ReadCommittedAsync(database, now.AddSeconds(-1), 64, default))
                .Any(item => item.Id == "admin:" + id && item.Action == "admin.kick"));
        }
        finally
        {
            await database.WithConnectionAsync(async (db, token) =>
            {
                await using var delete = db.CreateCommand();
                delete.CommandText = "DELETE FROM ano_admin_action_audit WHERE audit_id = @id";
                AddParameter(delete, "@id", id);
                await delete.ExecuteNonQueryAsync(token);
                return true;
            });
        }
    }
    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
