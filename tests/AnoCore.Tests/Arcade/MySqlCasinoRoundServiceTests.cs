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
    public async Task SpitzePaidSpinsAndConcurrentCollectSettleOnceAcrossServiceRecreation()
    {
        var start = Start(CasinoRoundGame.Spitze);
        var first = await Service().ApplyAsync(start);
        Assert.AreEqual(1900L, first.Balance.Allocated);
        Assert.AreEqual(first.Round, await Service().ReadAsync(Player));
        var spin = Next(first.Round, CasinoRoundAction.Spin);
        var spins = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Service().ApplyAsync(spin).AsTask()));
        Assert.IsTrue(spins.All(x => x == spins[0]));
        Assert.AreEqual(1800L, spins[0].Balance.Allocated);
        Assert.AreEqual(200L, spins[0].Round.TotalStake);

        var collect = Next(spins[0].Round, CasinoRoundAction.Collect);
        var collects = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Service().ApplyAsync(collect).AsTask()));
        Assert.IsTrue(collects.All(x => x == collects[0]));
        Assert.AreEqual(1800L + collects[0].Round.Payout, collects[0].Balance.Allocated);
        Assert.AreEqual(collects[0], await Service().ApplyAsync(collect));
        Assert.IsNull(await Service().ReadAsync(Player));
        Assert.AreEqual(3L, await CountActions());
    }

    [TestMethod]
    public async Task SpitzeSevenPersistedActionGamesRechargeNothingAndFinishOnce()
    {
        var seeded = SpitzeRules.Start(100, SpitzeSymbol.Knife);
        for (var i = 0; i < 4; i++)
            seeded = SpitzeRules.Spin(seeded, seeded.Revision, SpitzeSymbol.Knife);
        Assert.AreEqual(7, seeded.ActionGamesRemaining);

        var store = new MySqlCasinoRoundStore(_database);
        var saved = await store.ApplyAsync(new(Guid.NewGuid(), Guid.NewGuid(), Player,
            CasinoRoundKind.Spitze, 0, "spitze-action-game-fixture"),
            _ => new(500, SpitzeRules.MaximumExposure(100)),
            _ => new(SpitzeRules.Version, JsonSerializer.Serialize(seeded), 500, 0,
                SpitzeRules.MaximumExposure(100), false, "{}"));

        var restored = await Service().ReadAsync(Player);
        Assert.IsNotNull(restored);
        Assert.AreEqual(saved.Round.StateJson, restored.StateJson);
        Assert.AreEqual(1500L, (await _wallet.ReadAsync(Player)).Allocated);

        var receipt = new DurableCasinoRoundReceipt(saved.OperationId, saved.Balance, restored);
        for (var i = 0; i < SpitzeRules.BonusSpins; i++)
        {
            var action = Next(receipt.Round, CasinoRoundAction.ActionGame);
            var duplicates = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Service().ApplyAsync(action).AsTask()));
            Assert.IsTrue(duplicates.All(x => x == duplicates[0]));
            receipt = duplicates[0];
            Assert.AreEqual(500L, receipt.Round.TotalStake);
            if (!receipt.Round.IsComplete)
                Assert.AreEqual(1500L, receipt.Balance.Allocated);
        }
        Assert.IsTrue(receipt.Round.IsComplete);
        Assert.AreEqual(1500L + receipt.Round.Payout, (await _wallet.ReadAsync(Player)).Allocated);
        Assert.IsNull(await Service().ReadAsync(Player));
        Assert.AreEqual(8L, await CountActions());
    }

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
    public async Task ExpandingBonusRestoresAndConcurrentFreeSpinsSettleExactlyOnce()
    {
        var spin = BiggestBoyExpandingRules.StartRound(100, _ => 0);
        await new MySqlCasinoRoundStore(_database).ApplyAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), Player, CasinoRoundKind.BiggestBoy, 0, "expanding-trigger-fixture"),
            _ => new(100, BiggestBoyExpandingRules.MaximumRoundExposure(100)),
            _ => new(BiggestBoyExpandingRules.Version, JsonSerializer.Serialize(spin), 100, 0,
                BiggestBoyExpandingRules.MaximumRoundExposure(100), false, "{}"));
        var restored = await Service().ReadAsync(Player);
        Assert.IsNotNull(restored);
        var receipt = await Service().ApplyAsync(Next(restored, CasinoRoundAction.FreeSpin));
        var turns = 1;
        while (!receipt.Round.IsComplete)
        {
            Assert.IsTrue(turns++ <= BiggestBoyExpandingRules.MaximumFreeSpinsAwarded);
            var action = Next(receipt.Round, CasinoRoundAction.FreeSpin);
            var duplicates = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Service().ApplyAsync(action).AsTask()));
            Assert.IsTrue(duplicates.All(value => value == duplicates[0]));
            receipt = duplicates[0];
            if (!receipt.Round.IsComplete)
            {
                Assert.AreEqual(1900L, receipt.Balance.Allocated);
                Assert.AreEqual(receipt.Round, await Service().ReadAsync(Player));
            }
        }
        Assert.AreEqual(1900L + receipt.Round.Payout, receipt.Balance.Allocated);
        Assert.IsNull(await Service().ReadAsync(Player));
        var result = JsonSerializer.Deserialize<BiggestBoySpinResult>(receipt.Round.StateJson)!;
        Assert.AreEqual(BiggestBoyExpandingRules.Version, result.State.RulesVersion);
        Assert.AreEqual(15, result.Grid.Length);
    }

    [TestMethod]
    public async Task CollectorBonusRestoresAndConcurrentFreeSpinsSettleExactlyOnce()
    {
        var spin = BiggestBoyCollectorRules.StartRound(100, _ => 0);
        await new MySqlCasinoRoundStore(_database).ApplyAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), Player, CasinoRoundKind.BiggestBoy, 0, "collector-trigger-fixture"),
            _ => new(100, BiggestBoyCollectorRules.MaximumRoundExposure(100)),
            _ => new(BiggestBoyCollectorRules.Version, JsonSerializer.Serialize(spin), 100, 0,
                BiggestBoyCollectorRules.MaximumRoundExposure(100), false, "{}"));
        var restored = await Service().ReadAsync(Player);
        Assert.IsNotNull(restored);
        var receipt = await Service().ApplyAsync(Next(restored, CasinoRoundAction.FreeSpin));
        var turns = 1;
        while (!receipt.Round.IsComplete)
        {
            Assert.IsTrue(turns++ <= BiggestBoyCollectorRules.MaximumFreeSpinsAwarded);
            var action = Next(receipt.Round, CasinoRoundAction.FreeSpin);
            var duplicates = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Service().ApplyAsync(action).AsTask()));
            Assert.IsTrue(duplicates.All(value => value == duplicates[0]));
            receipt = duplicates[0];
            if (!receipt.Round.IsComplete)
            {
                Assert.AreEqual(1900L, receipt.Balance.Allocated);
                Assert.AreEqual(receipt.Round, await Service().ReadAsync(Player));
            }
        }
        Assert.AreEqual(1900L + receipt.Round.Payout, receipt.Balance.Allocated);
        Assert.IsNull(await Service().ReadAsync(Player));
        var result = JsonSerializer.Deserialize<BiggestBoySpinResult>(receipt.Round.StateJson)!;
        Assert.AreEqual(BiggestBoyCollectorRules.Version, result.State.RulesVersion);
        Assert.AreEqual(15, result.Grid.Length);
    }

    [TestMethod]
    public async Task BiggestBoyTriggeredBonusRestoresAfterLimitReductionAndReplaysExactlyOnce()
    {
        var spin = BiggestBoyRules.StartRound(100, _ => 0);
        var stored = await new MySqlCasinoRoundStore(_database).ApplyAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), Player, CasinoRoundKind.BiggestBoy, 0, "biggest-trigger-fixture"),
            _ => new(100, BiggestBoyRules.MaximumRoundExposure(100)),
            _ => new(BiggestBoyRules.Version, JsonSerializer.Serialize(spin), 100, 0,
                BiggestBoyRules.MaximumRoundExposure(100), false, "{}"));
        var reduced = new CasinoRoundService(new MySqlCasinoRoundStore(_database), 50);
        var restored = await reduced.ReadAsync(Player);
        Assert.AreEqual(CasinoRoundGame.BiggestBoy, restored!.Game);
        Assert.AreEqual(stored.Round.StateJson, restored.StateJson);
        await Assert.ThrowsExactlyAsync<CasinoRejectedException>(async () => await reduced.ApplyAsync(
            Next(restored, CasinoRoundAction.FreeSpin) with { Stake = 50 }));
        await Assert.ThrowsExactlyAsync<CasinoRejectedException>(async () => await reduced.ApplyAsync(
            Next(restored, CasinoRoundAction.FreeSpin) with { Game = CasinoRoundGame.Book }));
        var choose = Next(restored, CasinoRoundAction.SelectSymbol) with { Choice = (int)BiggestBoySymbol.Beer };
        var chosen = await reduced.ApplyAsync(choose);
        Assert.AreEqual(chosen, await reduced.ApplyAsync(choose));
        Assert.AreEqual(1900L, chosen.Balance.Allocated);
        var chosenState = JsonSerializer.Deserialize<BiggestBoySpinResult>(chosen.Round.StateJson)!;
        Assert.AreEqual(BiggestBoySymbol.Beer, chosenState.State.ExpandingSymbol);
        var receipt = chosen;
        var spins = 0;
        while (!receipt.Round.IsComplete)
        {
            Assert.IsTrue(spins++ < BiggestBoyRules.MaximumFreeSpinsAwarded);
            var action = Next(receipt.Round, CasinoRoundAction.FreeSpin);
            var concurrent = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => reduced.ApplyAsync(action).AsTask()));
            Assert.IsTrue(concurrent.All(result => result == concurrent[0]));
            receipt = concurrent[0];
            if (!receipt.Round.IsComplete)
            {
                Assert.AreEqual(1900L, receipt.Balance.Allocated);
                Assert.AreEqual(receipt.Round, await Service().ReadAsync(Player));
            }
        }
        Assert.AreEqual(1900L + receipt.Round.Payout, (await _wallet.ReadAsync(Player)).Allocated);
        Assert.AreEqual(spins + 2L, await CountActions());
        Assert.IsNull(await reduced.ReadAsync(Player));
        await Assert.ThrowsExactlyAsync<CasinoRejectedException>(async () => await reduced.ApplyAsync(Start(CasinoRoundGame.BiggestBoy)));
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
            var state = JsonSerializer.Deserialize<BlackjackState>(receipt.Round.StateJson)!;
            last = Next(receipt.Round, state.Status == BlackjackRoundStatus.InsuranceOffer
                ? CasinoRoundAction.DeclineInsurance : CasinoRoundAction.Stand);
            receipt = await Service().ApplyAsync(last);
        }
        if (last is not null) Assert.AreEqual(receipt, await Service().ApplyAsync(last));
        Assert.AreEqual(1900L + receipt.Round.Payout, (await _wallet.ReadAsync(Player)).Allocated);
        Assert.AreEqual(100L, receipt.Round.TotalStake);
        Assert.IsNull(await Service().ReadAsync(Player));
    }

    [TestMethod]
    public async Task InsuranceRestoresAndConcurrentReplayDebitsAndPaysExactlyOnce()
    {
        var state = BlackjackExtendedRulesTests.InsuranceRound(true);
        var store = new MySqlCasinoRoundStore(_database);
        var first = await store.ApplyAsync(new(Guid.NewGuid(), Guid.NewGuid(), Player, CasinoRoundKind.Blackjack, 0, "insurance-fixture"),
            _ => new(100, 1700), _ => new(state.RulesVersion, JsonSerializer.Serialize(state), 100, 0, 1700, false, "{}"));
        var restored = (await Service().ReadAsync(Player))!;
        Assert.AreEqual(first.Round.StateJson, restored.StateJson);
        var action = Next(restored, CasinoRoundAction.Insurance);
        var receipts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Service().ApplyAsync(action).AsTask()));
        Assert.IsTrue(receipts.All(receipt => receipt == receipts[0]));
        Assert.AreEqual(150L, receipts[0].Round.TotalStake);
        Assert.AreEqual(150L, receipts[0].Round.Payout);
        Assert.AreEqual(2000L, (await _wallet.ReadAsync(Player)).Allocated);
        Assert.AreEqual(receipts[0], await Service().ApplyAsync(action));
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
