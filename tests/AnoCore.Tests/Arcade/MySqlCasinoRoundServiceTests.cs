using System.Text.Json;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Arcade.Casino;
using AnoCore.Runtime.Persistence;

namespace AnoCore.Tests.Arcade;

[TestClass]
[DoNotParallelize]
public sealed class MySqlCasinoRoundServiceTests
{
    private static readonly PlayerId Player = new(76561198000911604);
    private MySqlDatabase _database = null!;
    private MySqlCasinoWallet _wallet = null!;
    private CasinoRoundService Service() => new(new MySqlCasinoRoundStore(_database));

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
        await Clear();
        await _wallet.ApplyAsync(new(Guid.NewGuid(), Player, CasinoAction.Credit, 2000, null));
        await _wallet.ApplyAsync(new(Guid.NewGuid(), Player, CasinoAction.Load, 2000, null));
    }

    [TestCleanup]
    public async Task Cleanup() { if (_database is not null) await Clear(); }

    [TestMethod]
    public async Task RiskStartRestoreBankAndConcurrentReplaySettleOnce()
    {
        var start = Start(CasinoRoundGame.Risk);
        var started = await Service().ApplyAsync(start);
        Assert.AreEqual(started.Round, await Service().ReadAsync(Player));
        var bank = Next(started.Round, CasinoRoundAction.Bank);
        var receipts = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Service().ApplyAsync(bank).AsTask()));
        Assert.IsTrue(receipts.All(receipt => receipt == receipts[0]));
        Assert.AreEqual(100L, receipts[0].Round.Payout);
        Assert.AreEqual(new CasinoBalance(0, 2000, 4), await _wallet.ReadAsync(Player));
        Assert.IsNull(await Service().ReadAsync(Player));
        Assert.AreEqual(receipts[0], await Service().ApplyAsync(bank));
        Assert.AreEqual(2L, await CountActions());
    }

    [TestMethod]
    public async Task BookStartAndEveryRemainingSpinChargeOnlyTheInitialStake()
    {
        var receipt = await Service().ApplyAsync(Start(CasinoRoundGame.Book));
        receipt = await FinishBook(receipt);
        Assert.AreEqual(1900L + receipt.Round.Payout, (await _wallet.ReadAsync(Player)).Allocated);
        Assert.AreEqual(100L, receipt.Round.TotalStake);
        Assert.IsNull(await Service().ReadAsync(Player));
    }

    [TestMethod]
    public async Task PersistedTriggeredBonusRestoresItsGridAndFinishesWithoutExtraDebit()
    {
        // Seed a guaranteed native trigger through the production transaction store, then
        // reconstruct the production service for each free-spin transition.
        var draws = 0;
        var spin = BonusSlotRules.StartRound(100, max => draws++ < 3 ? 7 : 0);
        Assert.AreEqual(10, spin.State.FreeSpinsRemaining);
        var roundId = Guid.NewGuid();
        var stored = await new MySqlCasinoRoundStore(_database).ApplyAsync(
            new(Guid.NewGuid(), roundId, Player, CasinoRoundKind.BonusSlot, 0, "trigger-fixture"),
            _ => new(100, BonusSlotRules.MaximumRoundExposure(100)),
            _ => new(BonusSlotRules.Version, JsonSerializer.Serialize(spin), 100, 0,
                BonusSlotRules.MaximumRoundExposure(100), false, "{}"));
        var restored = await Service().ReadAsync(Player);
        Assert.AreEqual(stored.Round.StateJson, restored!.StateJson);
        var receipt = await FinishBook(new(stored.OperationId, stored.Balance, restored));
        Assert.AreEqual(1900L + receipt.Round.Payout, (await _wallet.ReadAsync(Player)).Allocated);
        Assert.IsTrue((await CountActions()) >= 11);
        Assert.IsTrue((await CountActions()) <= 31);
    }

    [TestMethod]
    public async Task BlackjackRestoreAndStandReachTerminalAndReplayDoesNotCreditTwice()
    {
        var receipt = await Service().ApplyAsync(Start(CasinoRoundGame.Blackjack));
        DurableCasinoRoundOperation? last = null;
        var actions = 0;
        while (!receipt.Round.IsComplete)
        {
            Assert.IsTrue(actions++ < BlackjackRules.MaximumHands);
            Assert.AreEqual(receipt.Round, await Service().ReadAsync(Player));
            last = Next(receipt.Round, CasinoRoundAction.Stand);
            receipt = await Service().ApplyAsync(last);
        }
        if (last is not null) Assert.AreEqual(receipt, await Service().ApplyAsync(last));
        Assert.AreEqual(1900L + receipt.Round.Payout, (await _wallet.ReadAsync(Player)).Allocated);
        Assert.AreEqual(100L, receipt.Round.TotalStake);
        Assert.IsNull(await Service().ReadAsync(Player));
    }

    private async Task<DurableCasinoRoundReceipt> FinishBook(DurableCasinoRoundReceipt receipt)
    {
        var spins = 0;
        while (!receipt.Round.IsComplete)
        {
            Assert.IsTrue(spins++ < BonusSlotRules.MaximumFreeSpinsAwarded);
            Assert.AreEqual(receipt.Round, await Service().ReadAsync(Player));
            var action = Next(receipt.Round, CasinoRoundAction.FreeSpin);
            receipt = await Service().ApplyAsync(action);
            Assert.AreEqual(receipt, await Service().ApplyAsync(action));
            if (!receipt.Round.IsComplete) Assert.AreEqual(1900L, receipt.Balance.Allocated);
        }
        return receipt;
    }

    private static DurableCasinoRoundOperation Start(CasinoRoundGame game) =>
        new(Guid.NewGuid(), Player, Guid.NewGuid(), game, CasinoRoundAction.Start, 100, 0);
    private static DurableCasinoRoundOperation Next(DurableCasinoRoundSnapshot round, CasinoRoundAction action) =>
        new(Guid.NewGuid(), Player, round.Id, round.Game, action, round.Stake, round.Revision);
    private async Task<long> CountActions() => await _database.WithConnectionAsync(async (connection, token) =>
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM ano_casino_round_actions WHERE player_steam_id={Player.SteamId64}";
        return Convert.ToInt64(await command.ExecuteScalarAsync(token));
    });
    private async Task Clear() => await _database.WithConnectionAsync(async (connection, token) =>
    {
        foreach (var table in new[] { "ano_casino_round_actions", "ano_casino_rounds", "ano_casino_operations", "ano_ap_accounts" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {table} WHERE player_steam_id={Player.SteamId64}";
            await command.ExecuteNonQueryAsync(token);
        }
        return true;
    });
}
