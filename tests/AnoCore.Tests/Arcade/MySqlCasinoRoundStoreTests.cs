using AnoCore.Abstractions.Players;
using AnoCore.Modules.Arcade.Casino;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.Arcade;

[TestClass]
[DoNotParallelize]
public sealed class MySqlCasinoRoundStoreTests
{
    private static readonly PlayerId Player = new(76561198000911556);
    private static readonly PlayerId OtherPlayer = new(76561198000911557);
    private MySqlDatabase _database = null!;
    private MySqlCasinoWallet _wallet = null!;

    [TestInitialize]
    public async Task Setup()
    {
        var connectionString = Environment.GetEnvironmentVariable("ANOCORE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Inconclusive("ANOCORE_TEST_MYSQL is not configured.");
        _database = new(connectionString!);
        _wallet = new(_database);
        await _database.WithConnectionAsync(async (connection, token) =>
        {
            await new CasinoSchemaMigration027().ApplyAsync(connection, token);
            await new CasinoSchemaMigration028().ApplyAsync(connection, token);
            return true;
        });
        await ClearAsync();
        await _wallet.ApplyAsync(new(Guid.NewGuid(), Player, CasinoAction.Credit, 2_000, null));
        await _wallet.ApplyAsync(new(Guid.NewGuid(), Player, CasinoAction.Load, 2_000, null));
    }

    [TestCleanup]
    public async Task Cleanup() { if (_database is not null) await ClearAsync(); }

    [TestMethod]
    public async Task ConcurrentDuplicateActionsDrawAndSettleExactlyOnceAcrossRestart()
    {
        var store = new MySqlCasinoRoundStore(_database);
        var roundId = Guid.NewGuid();
        var start = new CasinoRoundOperation(Guid.NewGuid(), roundId, Player, CasinoRoundKind.Risk, 0, "{\"action\":\"start\"}");
        var transitions = 0;
        var starts = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => store.ApplyAsync(start,
            _ => new(100, 200), state =>
        {
            Assert.IsNull(state);
            Interlocked.Increment(ref transitions);
            return new("anocasino-risk-v1", "{\"held\":100}", 100, 0, 200, false, "{\"held\":100}");
        }).AsTask()));

        Assert.AreEqual(1, transitions);
        Assert.IsTrue(starts.All(x => x == starts[0]));
        Assert.AreEqual(1, starts[0].Round.Revision);
        Assert.AreEqual(1_900, starts[0].Balance.Allocated);
        var recovered = await new MySqlCasinoRoundStore(_database).ReadActiveAsync(Player);
        Assert.AreEqual(starts[0].Round, recovered);

        var bank = new CasinoRoundOperation(Guid.NewGuid(), roundId, Player, CasinoRoundKind.Risk, 1, "{\"action\":\"bank\"}");
        var banks = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => new MySqlCasinoRoundStore(_database).ApplyAsync(bank,
            _ => new(0, 100), state =>
        {
            Assert.IsNotNull(state);
            return new("anocasino-risk-v1", "{\"held\":100,\"done\":true}", 0, 100, 0, true, "{\"banked\":100}");
        }).AsTask()));
        Assert.IsTrue(banks.All(x => x == banks[0]));
        Assert.AreEqual(CasinoRoundStatus.Completed, banks[0].Round.Status);
        Assert.AreEqual(2_000, banks[0].Balance.Allocated);
        Assert.IsNull(await store.ReadActiveAsync(Player));
    }

    [TestMethod]
    public async Task CompetingExpectedRevisionActionsAcceptExactlyOne()
    {
        var store = new MySqlCasinoRoundStore(_database);
        var roundId = Guid.NewGuid();
        await store.ApplyAsync(new(Guid.NewGuid(), roundId, Player, CasinoRoundKind.BonusSlot, 0, "{\"action\":\"start\"}"),
            _ => new(100, 10_000),
            _ => new("anocore-book-5x3-v1", "{\"remaining\":10}", 100, 0, 10_000, false, "{\"remaining\":10}"));

        async Task<bool> SpinAsync()
        {
            try
            {
                await new MySqlCasinoRoundStore(_database).ApplyAsync(
                    new(Guid.NewGuid(), roundId, Player, CasinoRoundKind.BonusSlot, 1, "{\"action\":\"spin\"}"),
                    _ => new(0, 9_000),
                    _ => new("anocore-book-5x3-v1", "{\"remaining\":9}", 0, 0, 9_000, false, "{\"remaining\":9}"));
                return true;
            }
            catch (CasinoRejectedException) { return false; }
        }

        var results = await Task.WhenAll(SpinAsync(), SpinAsync());
        Assert.AreEqual(1, results.Count(x => x));
        Assert.AreEqual(2, (await store.ReadActiveAsync(Player))!.Revision);
    }

    [TestMethod]
    public async Task NonTerminalPayoutIsRejectedWithoutChangingBalanceOrRound()
    {
        var store = new MySqlCasinoRoundStore(_database);
        var before = await _wallet.ReadAsync(Player);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () => await store.ApplyAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), Player, CasinoRoundKind.Risk, 0, "{\"action\":\"start\"}"),
            _ => new(100, 200),
            _ => new("anocasino-risk-v1", "{\"held\":100}", 100, 1, 199, false, "{\"held\":100}")));

        Assert.AreEqual(before, await _wallet.ReadAsync(Player));
        Assert.IsNull(await store.ReadActiveAsync(Player));
    }

    [TestMethod]
    public async Task SameOperationIdIsIdempotentPerPlayerWithoutCrossPlayerCollision()
    {
        await _wallet.ApplyAsync(new(Guid.NewGuid(), OtherPlayer, CasinoAction.Credit, 500, null));
        await _wallet.ApplyAsync(new(Guid.NewGuid(), OtherPlayer, CasinoAction.Load, 500, null));
        var id = Guid.NewGuid();
        var transitions = 0;

        async Task<CasinoRoundReceipt> StartAsync(PlayerId player) => await new MySqlCasinoRoundStore(_database).ApplyAsync(
            new(id, Guid.NewGuid(), player, CasinoRoundKind.Risk, 0, "{\"action\":\"start\"}"),
            _ => new(100, 200),
            _ =>
            {
                Interlocked.Increment(ref transitions);
                return new("anocasino-risk-v1", "{\"held\":100}", 100, 0, 200, false, "{\"held\":100}");
            });

        var receipts = await Task.WhenAll(StartAsync(Player), StartAsync(OtherPlayer));
        Assert.AreEqual(2, transitions);
        Assert.AreEqual(2, receipts.Length);
        Assert.AreNotEqual(receipts[0].Round.Player, receipts[1].Round.Player);
    }

    private async Task ClearAsync() => await _database.WithConnectionAsync(async (connection, token) =>
    {
        foreach (var table in new[] { "ano_casino_round_actions", "ano_casino_rounds", "ano_casino_operations", "ano_ap_accounts" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {table} WHERE player_steam_id IN ({Player.SteamId64},{OtherPlayer.SteamId64})";
            await command.ExecuteNonQueryAsync(token);
        }
        return true;
    });
}
