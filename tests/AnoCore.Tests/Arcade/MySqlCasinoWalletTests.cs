using AnoCore.Abstractions.Persistence;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Arcade.Casino;
using AnoCore.Runtime.Persistence;
namespace AnoCore.Tests.Arcade;

[TestClass]
[DoNotParallelize]
public sealed class MySqlCasinoWalletTests
{
    private MySqlDatabase _db = null!;
    private MySqlCasinoWallet _wallet = null!;
    private static readonly PlayerId Player = new(76561198000911555);
    [TestInitialize]
    public async Task Setup()
    {
        var cs = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(cs)) Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured.");
        _db = new(cs!); _wallet = new(_db);
        await _db.WithConnectionAsync(async (c, t) =>
        {
            await new CasinoSchemaMigration027().ApplyAsync(c, t);
            await new CasinoSchemaMigration028().ApplyAsync(c, t);
            return true;
        });
        await Clear();
    }
    [TestCleanup] public async Task Cleanup() { if (_db is not null) await Clear(); }
    private async Task Clear() => await _db.WithConnectionAsync(async (c, t) =>
    {
        foreach (var table in new[] { "ano_casino_round_actions", "ano_casino_rounds", "ano_casino_operations", "ano_ap_accounts" }) { await using var cmd = c.CreateCommand(); cmd.CommandText = $"DELETE FROM {table} WHERE player_steam_id={Player.SteamId64}"; await cmd.ExecuteNonQueryAsync(t); }
        return true;
    });
    private ValueTask<CasinoReceipt> Credit(int amount) => _wallet.ApplyAsync(new(Guid.NewGuid(), Player, CasinoAction.Credit, amount, null));
    [TestMethod]
    public async Task BootstrapRepairsMissingPhysicalCasinoTables()
    {
        await _db.WithConnectionAsync(async (connection, token) =>
        {
            foreach (var table in new[]
            {
                "ano_casino_round_actions",
                "ano_casino_rounds",
                "ano_casino_operations",
                "ano_ap_accounts",
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE IF EXISTS {table}";
                await command.ExecuteNonQueryAsync(token);
            }

            return true;
        });

        await CasinoPersistenceBootstrap.EnsureReadyAsync(_db);
        await Credit(250);

        Assert.AreEqual(new CasinoBalance(250, 0, 1), await _wallet.ReadAsync(Player));
    }

    [TestMethod]
    public async Task TransfersConserveCredits_AndSurviveRepositoryRestart()
    {
        Assert.AreEqual(new CasinoBalance(0, 0, 0), await _wallet.ReadAsync(Player));
        await Credit(1000); await _wallet.ApplyAsync(new(Guid.NewGuid(), Player, CasinoAction.Load, 500, null));
        Assert.AreEqual(new CasinoBalance(500, 500, 2), await new MySqlCasinoWallet(_db).ReadAsync(Player));
        await _wallet.ApplyAsync(new(Guid.NewGuid(), Player, CasinoAction.Return, 0, null));
        Assert.AreEqual(new CasinoBalance(1000, 0, 3), await _wallet.ReadAsync(Player));
    }
    [TestMethod]
    public async Task DuplicateGameRequestsReplayOneResultAndOneDebit()
    {
        await Credit(1000); await _wallet.ApplyAsync(new(Guid.NewGuid(), Player, CasinoAction.Load, 1000, null));
        var op = new CasinoOperation(Guid.NewGuid(), Player, CasinoAction.Play, 0, new(CasinoGame.Coinflip, 100, 0));
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => _wallet.ApplyAsync(op).AsTask()));
        Assert.IsTrue(results.All(r => r.Id == results[0].Id && r.Outcome!.Number == results[0].Outcome!.Number && r.Outcome!.Payout == results[0].Outcome!.Payout && r.Balance == results[0].Balance));
        var balance = await _wallet.ReadAsync(Player);
        Assert.AreEqual(900 + results[0].Outcome!.Payout, balance.Allocated); Assert.AreEqual(3L, balance.Revision);
        Assert.AreEqual(3, (await _wallet.HistoryAsync(Player)).Count);
    }
    [TestMethod]
    public async Task ConcurrentTransfersCannotOverspend_AndRejectedWritesRollback()
    {
        await Credit(1000);
        async Task<bool> Load() { try { await _wallet.ApplyAsync(new(Guid.NewGuid(), Player, CasinoAction.Load, 700, null)); return true; } catch (CasinoRejectedException) { return false; } }
        var outcomes = await Task.WhenAll(Load(), Load()); Assert.AreEqual(1, outcomes.Count(x => x));
        Assert.AreEqual(new CasinoBalance(300, 700, 2), await _wallet.ReadAsync(Player));
    }
    [TestMethod]
    public async Task OperationIdCannotBeReusedWithDifferentAmount_AndFailedPlayHasNoLedger()
    {
        var id = Guid.NewGuid(); await _wallet.ApplyAsync(new(id, Player, CasinoAction.Credit, 100, null));
        await Assert.ThrowsExactlyAsync<CasinoRejectedException>(async () => await _wallet.ApplyAsync(new(id, Player, CasinoAction.Credit, 200, null)));
        await Assert.ThrowsExactlyAsync<CasinoRejectedException>(async () => await _wallet.ApplyAsync(new(Guid.NewGuid(), Player, CasinoAction.Play, 0, new(CasinoGame.Slots, 100, 0))));
        Assert.AreEqual(new CasinoBalance(100, 0, 1), await _wallet.ReadAsync(Player)); Assert.AreEqual(1, (await _wallet.HistoryAsync(Player)).Count);
    }

    [TestMethod]
    public async Task ActiveRoundReservationProtectsAccountLimitFromLegacyCredits()
    {
        await _db.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO ano_ap_accounts VALUES ({Player.SteamId64},999999900,0,1)";
            await command.ExecuteNonQueryAsync(token);
            return true;
        });
        var store = new MySqlCasinoRoundStore(_db);
        await store.ApplyAsync(new(Guid.NewGuid(), Guid.NewGuid(), Player, CasinoRoundKind.Risk, 0, "{\"action\":\"start\"}"),
            _ => new(0, 100),
            _ => new("anocasino-risk-v1", "{\"held\":100}", 0, 0, 100, false, "{\"started\":true}"));

        await Assert.ThrowsExactlyAsync<CasinoRejectedException>(async () =>
            await _wallet.ApplyAsync(new(Guid.NewGuid(), Player, CasinoAction.Credit, 1, null)));
        Assert.AreEqual(999_999_900, (await _wallet.ReadAsync(Player)).Available);
    }
}
