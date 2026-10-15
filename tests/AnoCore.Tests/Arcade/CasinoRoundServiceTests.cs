using AnoCore.Abstractions.Players;
using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class CasinoRoundServiceTests
{
    private static readonly PlayerId Player = new(76561198000911603);

    [TestMethod]
    public async Task IndependentControlsArePersistedAndAcceptedBonusSurvivesConfigChange()
    {
        foreach (var game in new[] { CasinoRoundGame.BiggestBoy, CasinoRoundGame.Book })
        {
            var store = new MemoryStore();
            var original = new SlotOutcomeSettings(0, 100, 0);
            var service = new CasinoRoundService(store, biggestBoyOutcomes: original, bookOutcomes: original);
            var op = new DurableCasinoRoundOperation(Guid.NewGuid(), Player, Guid.NewGuid(), game, CasinoRoundAction.Start, 100, 0);
            var first = await service.ApplyAsync(op);
            Assert.IsFalse(first.Round.IsComplete);
            Assert.AreEqual(100L, first.Round.TotalStake);
            Assert.AreEqual(game == CasinoRoundGame.BiggestBoy ? ControlledSlotRules.BiggestVersion : ControlledSlotRules.BookVersion, store.Current!.RulesVersion);
            service = new CasinoRoundService(store, biggestBoyOutcomes: new(100, 0, 100), bookOutcomes: new(100, 0, 100));
            var next = await service.ApplyAsync(op with
            {
                Id = Guid.NewGuid(),
                SessionId = first.Round.Id,
                Action = CasinoRoundAction.FreeSpin,
                ExpectedRevision = first.Round.Revision
            });
            Assert.AreEqual(0L, store.LastPlan!.AdditionalDebit);
            if (game == CasinoRoundGame.BiggestBoy)
            {
                var state = System.Text.Json.JsonSerializer.Deserialize<BiggestBoySpinResult>(next.Round.StateJson)!;
                Assert.AreEqual(original, state.State.OutcomeSettings);
                Assert.AreEqual(0L, state.Payout);
            }
            else
            {
                var state = System.Text.Json.JsonSerializer.Deserialize<BonusSlotSpinResult>(next.Round.StateJson)!;
                Assert.AreEqual(original, state.State.OutcomeSettings);
                Assert.AreEqual(0L, state.Payout);
            }
        }
    }

    [TestMethod]
    public async Task HistoricalControlledBonusesRetainVersionAndExposureAfterUpgrade()
    {
        foreach (var game in new[] { CasinoRoundGame.BiggestBoy, CasinoRoundGame.Book })
        {
            var store = new MemoryStore();
            var settings = new SlotOutcomeSettings(0, 100, 0);
            var service = new CasinoRoundService(store, biggestBoyOutcomes: settings, bookOutcomes: settings);
            var op = new DurableCasinoRoundOperation(Guid.NewGuid(), Player, Guid.NewGuid(), game, CasinoRoundAction.Start, 100, 0);
            await service.ApplyAsync(op);
            var version = game == CasinoRoundGame.BiggestBoy ? ControlledSlotRules.LegacyBiggestVersion : ControlledSlotRules.LegacyBookVersion;
            var currentVersion = game == CasinoRoundGame.BiggestBoy ? ControlledSlotRules.BiggestVersion : ControlledSlotRules.BookVersion;
            var exposure = game == CasinoRoundGame.BiggestBoy ? BiggestBoyExpandingRules.MaximumRoundExposure(100) : BonusSlotRules.MaximumRoundExposure(100);
            store.Seed(store.Current! with
            {
                RulesVersion = version,
                ReservedPayout = exposure,
                StateJson = store.Current!.StateJson.Replace(currentVersion, version, StringComparison.Ordinal)
            });
            var receipt = await service.ApplyAsync(op with
            {
                Id = Guid.NewGuid(),
                Action = CasinoRoundAction.FreeSpin,
                ExpectedRevision = 1
            });
            Assert.AreEqual(version, store.Current!.RulesVersion);
            Assert.AreEqual(exposure, store.LastPlan!.MaximumPayoutExposure);
            Assert.AreEqual(0L, store.LastPlan.AdditionalDebit);
            Assert.IsFalse(receipt.Round.IsComplete);
        }
    }

    [TestMethod]
    public async Task HighBiggestBoyStakeReservesNewExposureAndKeepsBonusStakeAfterLimitReduction()
    {
        var store = new MemoryStore(200000);
        var service = new CasinoRoundService(store, biggestBoyMaximumStake: 100000, biggestBoyOutcomes: new(0, 100, 0));
        var op = new DurableCasinoRoundOperation(Guid.NewGuid(), Player, Guid.NewGuid(), CasinoRoundGame.BiggestBoy, CasinoRoundAction.Start, 100000, 0);
        var first = await service.ApplyAsync(op);
        Assert.AreEqual(155000000L, store.LastPlan!.MaximumPayoutExposure);
        Assert.AreEqual(100000L, store.LastPlan.AdditionalDebit);
        Assert.AreEqual(100000, first.Round.Stake);
        Assert.AreEqual(100000L, first.Balance.Allocated);
        service = new CasinoRoundService(store, biggestBoyMaximumStake: 100, biggestBoyOutcomes: new(100, 0, 100));
        var next = await service.ApplyAsync(op with { Id = Guid.NewGuid(), Action = CasinoRoundAction.FreeSpin, ExpectedRevision = 1 });
        Assert.AreEqual(100000, next.Round.Stake);
        Assert.AreEqual(0L, store.LastPlan.AdditionalDebit);
        Assert.AreEqual(155000000L, store.LastPlan.MaximumPayoutExposure);
        Assert.AreEqual(100000L, next.Balance.Allocated);
        Assert.IsFalse(next.Round.IsComplete);
        var low = new CasinoRoundService(new MemoryStore(200000), biggestBoyMaximumStake: 10000, biggestBoyOutcomes: new(0, 100, 0));
        await Assert.ThrowsExactlyAsync<CasinoRejectedException>(async () => await low.ApplyAsync(op));
    }

    [TestMethod]
    public async Task HighBiggestBoyFullScreenSettlesFromItsActualGrid()
    {
        var store = new MemoryStore(200000);
        var service = new CasinoRoundService(store, biggestBoyMaximumStake: 100000, biggestBoyOutcomes: new(100, 0, 100));
        var receipt = await service.ApplyAsync(new(Guid.NewGuid(), Player, Guid.NewGuid(), CasinoRoundGame.BiggestBoy, CasinoRoundAction.Start, 100000, 0));
        var spin = System.Text.Json.JsonSerializer.Deserialize<BiggestBoySpinResult>(receipt.Round.StateJson)!;
        Assert.IsTrue(receipt.Round.IsComplete);
        Assert.AreEqual(1, spin.Grid.Distinct().Count());
        Assert.AreEqual(100000L * BiggestBoyBalancedRules.Multiplier(spin.Grid[0], 5), spin.Payout);
        Assert.AreEqual(100000L + spin.Payout, receipt.Balance.Allocated);
        Assert.AreEqual(0L, store.Current!.ReservedPayout);
        Assert.AreEqual(155000000L, store.LastPlan!.MaximumPayoutExposure);
        Assert.IsTrue(BiggestBoyStakeLimits.IsValid(100000));
        Assert.IsFalse(BiggestBoyStakeLimits.IsValid(100050));
        Assert.AreEqual(750, BiggestBoyStakeLimits.Step(500, 1, 750));
    }

    [TestMethod]
    public async Task EveryRoundGameEnforcesItsConfiguredStartingLimitBeforeDrawing()
    {
        foreach (var game in new[] { CasinoRoundGame.Blackjack, CasinoRoundGame.Risk, CasinoRoundGame.Book })
        {
            var store = new MemoryStore();
            var service = new CasinoRoundService(store, stakeLimits: new() { Blackjack = 1000, Risk = 1000, Book = 1000 });
            var op = new DurableCasinoRoundOperation(Guid.NewGuid(), Player, Guid.NewGuid(), game, CasinoRoundAction.Start, 1050, 0);
            await Assert.ThrowsExactlyAsync<CasinoRejectedException>(async () => await service.ApplyAsync(op));
            Assert.AreEqual(0, store.Transitions);
            var receipt = await service.ApplyAsync(op with { Id = Guid.NewGuid(), Stake = 1000 });
            Assert.AreEqual(1000, receipt.Round.Stake);
            Assert.AreEqual(1000L, receipt.Round.TotalStake);
        }
    }

    [TestMethod]
    public async Task LowerBookLimitStillAllowsAcceptedBonusWithoutAnotherDebit()
    {
        var spin = BonusSlotRules.StartRound(1000, bound => bound - 1);
        Assert.IsTrue(spin.State.FreeSpinsRemaining > 0);
        var store = new MemoryStore();
        store.Seed(new(Guid.NewGuid(), Player, CasinoRoundKind.BonusSlot, BonusSlotRules.Version,
            System.Text.Json.JsonSerializer.Serialize(spin), CasinoRoundStatus.Active, 1, BonusSlotRules.MaximumRoundExposure(1000)));
        var service = new CasinoRoundService(store, stakeLimits: new() { Book = 50 });
        var receipt = await service.ApplyAsync(new(Guid.NewGuid(), Player, store.Current!.RoundId,
            CasinoRoundGame.Book, CasinoRoundAction.FreeSpin, 1000, 1));
        Assert.AreEqual(1000, receipt.Round.Stake);
        Assert.AreEqual(0L, store.LastPlan!.AdditionalDebit);
    }

    [TestMethod]
    public async Task ConfiguredProfileIsAppliedOnlyToNewRoundsAndFrozenAcrossServiceRestart()
    {
        var store = new MemoryStore();
        var service = new CasinoRoundService(store, biggestBoyProfile: "generous");
        var op = new DurableCasinoRoundOperation(Guid.NewGuid(), Player, Guid.NewGuid(),
            CasinoRoundGame.BiggestBoy, CasinoRoundAction.Start, 100, 0);
        var receipt = await service.ApplyAsync(op);
        Assert.AreEqual("generous", System.Text.Json.JsonSerializer.Deserialize<BiggestBoySpinResult>(receipt.Round.StateJson)!.State.Profile);
        var bonus = BiggestBoyExpandingRules.StartRound(100, _ => 0, "frequent");
        store = new MemoryStore();
        store.Seed(new(Guid.NewGuid(), Player, CasinoRoundKind.BiggestBoy, BiggestBoyExpandingRules.Version,
            System.Text.Json.JsonSerializer.Serialize(bonus), CasinoRoundStatus.Active, 1, BiggestBoyExpandingRules.MaximumRoundExposure(100)));
        service = new CasinoRoundService(store, biggestBoyProfile: "generous");
        receipt = await service.ApplyAsync(op with { Id = Guid.NewGuid(), SessionId = store.Current!.RoundId, Action = CasinoRoundAction.FreeSpin, ExpectedRevision = 1 });
        Assert.AreEqual("frequent", System.Text.Json.JsonSerializer.Deserialize<BiggestBoySpinResult>(receipt.Round.StateJson)!.State.Profile);
        Assert.AreEqual(0L, store.LastPlan!.AdditionalDebit);
        Assert.AreEqual(1000L, receipt.Balance.Allocated);
    }

    [TestMethod]
    public async Task BiggestBoyHasSeparateVersionAndConfigurableStakeWhileBookKeepsItsLimit()
    {
        var store = new MemoryStore();
        var service = new CasinoRoundService(store, 1000);
        var op = new DurableCasinoRoundOperation(Guid.NewGuid(), Player, Guid.NewGuid(),
            CasinoRoundGame.BiggestBoy, CasinoRoundAction.Start, 1000, 0);
        var receipt = await service.ApplyAsync(op);
        Assert.AreEqual(CasinoRoundGame.BiggestBoy, receipt.Round.Game);
        Assert.AreEqual(CasinoRoundKind.BiggestBoy, store.Current!.Kind);
        Assert.AreEqual(BiggestBoyExpandingRules.Version, store.Current.RulesVersion);
        Assert.AreEqual(1000L, receipt.Round.TotalStake);
        Assert.AreEqual(BiggestBoyExpandingRules.MaximumRoundExposure(1000), store.LastPlan!.MaximumPayoutExposure);
        Assert.AreEqual(1000, System.Text.Json.JsonSerializer.Deserialize<BiggestBoySpinResult>(receipt.Round.StateJson)!.State.LockedStake);
        var before = store.Transitions;
        await Assert.ThrowsExactlyAsync<CasinoRejectedException>(async () => await service.ApplyAsync(op with
        { Id = Guid.NewGuid(), SessionId = Guid.NewGuid(), Stake = 1050 }));
        await Assert.ThrowsExactlyAsync<CasinoRejectedException>(async () => await service.ApplyAsync(op with
        { Id = Guid.NewGuid(), SessionId = Guid.NewGuid(), Game = CasinoRoundGame.Book }));
        Assert.AreEqual(before, store.Transitions);
    }

    [TestMethod]
    public async Task ExistingBiggestBoyV2KeepsVersionAndSelectionWithoutNewDebit()
    {
        var spin = BiggestBoyRules.StartRound(100, _ => 0);
        var store = new MemoryStore();
        store.Seed(new(Guid.NewGuid(), Player, CasinoRoundKind.BiggestBoy, BiggestBoyRules.Version,
            System.Text.Json.JsonSerializer.Serialize(spin), CasinoRoundStatus.Active, 1, BiggestBoyRules.MaximumRoundExposure(100)));
        var service = new CasinoRoundService(store);
        var op = new DurableCasinoRoundOperation(Guid.NewGuid(), Player, store.Current!.RoundId,
            CasinoRoundGame.BiggestBoy, CasinoRoundAction.SelectSymbol, 100, 1, (int)BiggestBoySymbol.Beer);
        var receipt = await service.ApplyAsync(op);
        Assert.AreEqual(BiggestBoyRules.Version, store.Current!.RulesVersion);
        Assert.AreEqual(0L, store.LastPlan!.AdditionalDebit);
        Assert.AreEqual(1000L, receipt.Balance.Allocated);
        var resumed = await service.ApplyAsync(op with { Id = Guid.NewGuid(), Action = CasinoRoundAction.FreeSpin, ExpectedRevision = 2 });
        Assert.AreEqual(25, System.Text.Json.JsonSerializer.Deserialize<BiggestBoySpinResult>(resumed.Round.StateJson)!.Grid.Length);
        Assert.AreEqual(BiggestBoyRules.Version, store.Current.RulesVersion);
    }

    [TestMethod]
    public async Task CollectorRoundResumesWithoutSelectionAndRetainsExposure()
    {
        var spin = BiggestBoyCollectorRules.StartRound(100, _ => 0);
        var store = new MemoryStore();
        store.Seed(new(Guid.NewGuid(), Player, CasinoRoundKind.BiggestBoy, BiggestBoyCollectorRules.Version,
            System.Text.Json.JsonSerializer.Serialize(spin), CasinoRoundStatus.Active, 1, BiggestBoyCollectorRules.MaximumRoundExposure(100)));
        var service = new CasinoRoundService(store);
        var op = new DurableCasinoRoundOperation(Guid.NewGuid(), Player, store.Current!.RoundId,
            CasinoRoundGame.BiggestBoy, CasinoRoundAction.SelectSymbol, 100, 1, (int)BiggestBoySymbol.Beer);
        await Assert.ThrowsExactlyAsync<CasinoRejectedException>(async () => await service.ApplyAsync(op));
        var receipt = await service.ApplyAsync(op with { Id = Guid.NewGuid(), Action = CasinoRoundAction.FreeSpin });
        Assert.AreEqual(BiggestBoyCollectorRules.Version, store.Current!.RulesVersion);
        Assert.AreEqual(0L, store.LastPlan!.AdditionalDebit);
        Assert.AreEqual(BiggestBoyCollectorRules.MaximumRoundExposure(100), store.LastPlan.MaximumPayoutExposure);
        Assert.AreEqual(1000L, receipt.Balance.Allocated);
        var played = System.Text.Json.JsonSerializer.Deserialize<BiggestBoySpinResult>(receipt.Round.StateJson)!;
        Assert.AreEqual(15, played.Grid.Length);
        Assert.AreEqual(1, played.State.FreeSpinsAwarded - played.State.FreeSpinsRemaining);
    }

    [TestMethod]
    public async Task ExpandingRoundFinalSpinKeepsLockedStakeAndSettlesFullAccumulatedPayout()
    {
        var spin = BiggestBoyExpandingRules.StartRound(100, _ => 0);
        spin = spin with { State = spin.State with { FreeSpinsAwarded = 30, FreeSpinsRemaining = 1, SpinsPlayed = 30, TotalPayout = 1234 } };
        var store = new MemoryStore();
        store.Seed(new(Guid.NewGuid(), Player, CasinoRoundKind.BiggestBoy, BiggestBoyExpandingRules.Version,
            System.Text.Json.JsonSerializer.Serialize(spin), CasinoRoundStatus.Active, 1, BiggestBoyExpandingRules.MaximumRoundExposure(100)));
        var service = new CasinoRoundService(store);
        var operation = new DurableCasinoRoundOperation(Guid.NewGuid(), Player, store.Current!.RoundId,
            CasinoRoundGame.BiggestBoy, CasinoRoundAction.FreeSpin, 100, 1);
        var receipt = await service.ApplyAsync(operation);
        var final = System.Text.Json.JsonSerializer.Deserialize<BiggestBoySpinResult>(receipt.Round.StateJson)!;
        Assert.IsTrue(receipt.Round.IsComplete);
        Assert.AreEqual(0L, store.LastPlan!.AdditionalDebit);
        Assert.AreEqual(BiggestBoyExpandingRules.MaximumRoundExposure(100), store.LastPlan.MaximumPayoutExposure);
        Assert.AreEqual(1234L + final.Payout, receipt.Round.Payout);
        Assert.AreEqual(1000L + receipt.Round.Payout, receipt.Balance.Allocated);
        Assert.AreEqual(0L, store.Current!.ReservedPayout);
        Assert.AreEqual(100, final.State.LockedStake);
        Assert.AreEqual(15, final.Grid.Length);
        Assert.AreEqual(BiggestBoyExpandingRules.Version, store.Current.RulesVersion);
    }

    [TestMethod]
    public async Task RiskDebitsOnceAndBankCreditsExactlyHeldStake()
    {
        var store = new MemoryStore();
        var service = new CasinoRoundService(store);
        var start = new DurableCasinoRoundOperation(Guid.NewGuid(), Player, Guid.NewGuid(),
            CasinoRoundGame.Risk, CasinoRoundAction.Start, 100, 0);
        var receipt = await service.ApplyAsync(start);
        Assert.AreEqual(900L, receipt.Balance.Allocated);
        Assert.AreEqual(3200L, store.Current!.ReservedPayout);
        Assert.AreEqual(receipt.Round, await new CasinoRoundService(store).ReadAsync(Player));
        var banked = await service.ApplyAsync(start with
        {
            Id = Guid.NewGuid(),
            Action = CasinoRoundAction.Bank,
            ExpectedRevision = receipt.Round.Revision,
        });
        Assert.IsTrue(banked.Round.IsComplete);
        Assert.AreEqual(100L, banked.Round.Payout);
        Assert.AreEqual(1000L, banked.Balance.Allocated);
        Assert.AreEqual(0L, store.Current!.ReservedPayout);
        Assert.IsNull(await service.ReadAsync(Player));
    }

    [TestMethod]
    public async Task WrongGameActionAndChangedStakeFailBeforeTransition()
    {
        var store = new MemoryStore();
        var service = new CasinoRoundService(store);
        var start = new DurableCasinoRoundOperation(Guid.NewGuid(), Player, Guid.NewGuid(),
            CasinoRoundGame.Risk, CasinoRoundAction.Start, 100, 0);
        await service.ApplyAsync(start);
        foreach (var action in new[]
        {
            start with { Id = Guid.NewGuid(), Action = CasinoRoundAction.Hit, ExpectedRevision = 1 },
            start with { Id = Guid.NewGuid(), Action = CasinoRoundAction.Bank, ExpectedRevision = 1, Stake = 150 },
        })
        {
            await Assert.ThrowsExactlyAsync<CasinoRejectedException>(async () => await service.ApplyAsync(action));
        }
        Assert.AreEqual(1, store.Transitions);
        Assert.AreEqual(900L, store.Allocated);
    }

    [TestMethod]
    public async Task BlackjackStartsWithUniqueCompleteShuffledDeckAndBoundedExposure()
    {
        var store = new MemoryStore();
        var service = new CasinoRoundService(store);
        var receipt = await service.ApplyAsync(new(Guid.NewGuid(), Player, Guid.NewGuid(),
            CasinoRoundGame.Blackjack, CasinoRoundAction.Start, 100, 0));
        var state = System.Text.Json.JsonSerializer.Deserialize<BlackjackState>(receipt.Round.StateJson)!;
        var cards = state.PlayerHands.SelectMany(hand => hand.Cards).Concat(state.DealerCards)
            .Concat(state.RemainingDeck).ToArray();
        Assert.AreEqual(52, cards.Length);
        Assert.AreEqual(52, cards.Distinct().Count());
        Assert.AreEqual(800L, store.LastPlan!.MaximumPayoutExposure);
        Assert.AreEqual(100L, receipt.Round.TotalStake);
    }

    private sealed class MemoryStore(long initialAllocated = 1000) : ICasinoRoundStore
    {
        public CasinoRoundSnapshot? Current { get; private set; }
        public void Seed(CasinoRoundSnapshot snapshot) => Current = snapshot;
        public long Allocated { get; private set; } = initialAllocated;
        public int Transitions { get; private set; }
        public CasinoRoundPlan? LastPlan { get; private set; }
        public ValueTask<CasinoRoundSnapshot?> ReadActiveAsync(PlayerId player, CancellationToken token = default)
            => ValueTask.FromResult(Current?.Status == CasinoRoundStatus.Active ? Current : null);
        public ValueTask<CasinoRoundReceipt> ApplyAsync(CasinoRoundOperation operation,
            Func<CasinoRoundSnapshot?, CasinoRoundPlan> plan, Func<CasinoRoundSnapshot?, CasinoRoundMutation> transition,
            CancellationToken token = default)
        {
            LastPlan = plan(Current);
            if (Allocated < LastPlan.AdditionalDebit) throw new CasinoRejectedException("Insufficient funds.");
            var mutation = transition(Current);
            Transitions++;
            Assert.AreEqual(LastPlan.AdditionalDebit, mutation.AdditionalDebit);
            Assert.IsTrue(mutation.Payout + mutation.ReservedPayout <= LastPlan.MaximumPayoutExposure);
            Allocated = Allocated - mutation.AdditionalDebit + mutation.Payout;
            Current = new(operation.RoundId, operation.Player, operation.Kind, mutation.RulesVersion,
                mutation.StateJson, mutation.Completed ? CasinoRoundStatus.Completed : CasinoRoundStatus.Active,
                operation.ExpectedRevision + 1, mutation.ReservedPayout);
            return ValueTask.FromResult(new CasinoRoundReceipt(operation.Id, Current,
                new CasinoBalance(0, Allocated, Transitions), mutation.OutcomeJson));
        }
    }
}
