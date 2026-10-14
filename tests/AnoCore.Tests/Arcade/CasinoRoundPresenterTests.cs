using System.Collections.Immutable;
using System.Text.Json;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Arcade.Casino;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Tests.AnoVeto;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class CasinoRoundPresenterTests
{
    private static readonly PlayerId Player = new(76561198000911901);

    [TestMethod]
    public async Task BlackjackCardsConcealDealerHoleAndClearAbsentHandsOnRefresh()
    {
        using var f = await Fixture.Create();
        var state = new BlackjackState(BlackjackRules.RulesVersion, 1, 100,
            [
                new([new(BlackjackSuit.Hearts, BlackjackRank.Ten), new(BlackjackSuit.Clubs, BlackjackRank.Seven)], 100, true, false, false),
                new([new(BlackjackSuit.Spades, BlackjackRank.Eight), new(BlackjackSuit.Diamonds, BlackjackRank.Eight)], 100, false, false, true)
            ],
            [new(BlackjackSuit.Clubs, BlackjackRank.Five), new(BlackjackSuit.Hearts, BlackjackRank.King)], [], 1, BlackjackRoundStatus.PlayerTurn, 0);
        var dealt = state.PlayerHands.SelectMany(hand => hand.Cards).Concat(state.DealerCards);
        state = state with
        {
            RemainingDeck = Enum.GetValues<BlackjackSuit>()
                .SelectMany(suit => Enum.GetValues<BlackjackRank>().Select(rank => new BlackjackCard(suit, rank)))
                .Except(dealt).ToImmutableArray()
        };
        f.Rounds.Current = new(Guid.NewGuid(), Player, CasinoRoundGame.Blackjack, 100, 1, false, 200, 0, JsonSerializer.Serialize(state));
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Blackjack);
        Assert.AreEqual("5\n♣", f.Text("dealer_card0"));
        Assert.AreEqual("?", f.Text("dealer_card1"));
        Assert.IsTrue(f.PanelClass("dealer_card1", "face-down"));
        Assert.IsFalse(f.PanelClass("dealer_card1", "red"), "The concealed suit must not leak.");
        Assert.IsFalse(f.PanelClass("dealer_card2", "present"));
        Assert.AreEqual("10\n♥", f.Text("hand0_card0"));
        Assert.IsTrue(f.PanelClass("hand0_card0", "red"));
        Assert.IsTrue(f.PanelClass("hand1", "active-hand"));
        Assert.IsFalse(f.PanelClass("hand0", "active-hand"));
        Assert.IsTrue(f.Text("hand1_summary")!.Contains("16"));

        state = state with
        {
            RemainingDeck = state.RemainingDeck.AddRange(state.PlayerHands[1].Cards),
            PlayerHands = [state.PlayerHands[0]],
            ActiveHandIndex = 0,
            Status = BlackjackRoundStatus.Settled
        };
        f.Rounds.Current = f.Rounds.Current with { StateJson = JsonSerializer.Serialize(state) };
        await f.Click("refresh");
        Assert.AreEqual("K\n♥", f.Text("dealer_card1"));
        Assert.IsFalse(f.PanelClass("dealer_card1", "face-down"));
        Assert.IsFalse(f.PanelClass("hand1", "present"));
        Assert.IsFalse(f.PanelClass("hand1_card0", "present"));
        Assert.AreEqual("", f.Text("hand1_card0"));
        Assert.AreEqual(0, f.Rounds.Calls.Count);
    }

    [TestMethod]
    public async Task BookSettlesBeforeEachStopAndResetsForNextFreeSpin()
    {
        using var f = await Fixture.Create();
        var spin = BonusSlotRules.StartRound(100, bound => bound - 1);
        f.Rounds.NextSnapshot = new(Guid.NewGuid(), Player, CasinoRoundGame.Book, 100, 1, false, 100, 0, JsonSerializer.Serialize(spin));
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Book);
        await f.Click("play");
        f.Clock.AdvanceMilliseconds(1550); f.Presenter.Tick();
        Assert.IsTrue(f.PanelClass("reel0", "settling"));
        Assert.IsFalse(f.PanelClass("reel0", "stopped"));
        Assert.IsFalse(f.PanelClass("reel1", "settling"));
        f.Clock.AdvanceMilliseconds(450); f.Presenter.Tick();
        Assert.IsTrue(f.PanelClass("reel0", "stopped"));
        Assert.IsFalse(f.PanelClass("reel0", "settling"));
        f.FinishAnimation();
        await f.Click("free");
        Assert.IsFalse(f.PanelClass("reel0", "stopped"));
        Assert.IsFalse(f.PanelClass("reel0", "settling"));
        Assert.IsTrue(f.Class("motion-b"));
        Assert.AreEqual(2, f.Rounds.Calls.Count);
    }

    [TestMethod]
    public async Task EveryRoundGameOpensWithRulesAndBookShowsPreviewBeforeBooking()
    {
        using var f = await Fixture.Create();
        foreach (var game in Enum.GetValues<CasinoRoundGame>())
        {
            Assert.IsTrue(await f.Presenter.OpenAsync(Player, game));
            Assert.AreEqual(1, f.Hud.VisiblePlayers(CasinoRoundPresenter.HudId).Count);
            Assert.IsFalse(string.IsNullOrWhiteSpace(f.Text("detail")));
            if (game == CasinoRoundGame.Book)
                for (var index = 0; index < 15; index++)
                    Assert.IsFalse(string.IsNullOrWhiteSpace(f.Text("symbol" + index)));
            Assert.AreEqual(0, f.Rounds.Calls.Count, "Opening a game must not place a stake.");
            f.Presenter.Close(Player);
        }
    }

    [TestMethod]
    public async Task StartAndBankUseStoredRoundRevision_AndAnimationCannotRepeatSettlement()
    {
        using var f = await Fixture.Create();
        Assert.IsTrue(await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk));
        await f.Click("plus");
        await f.Click("play");
        Assert.AreEqual(150, f.Rounds.Last!.Stake);
        Assert.AreEqual(CasinoRoundAction.Start, f.Rounds.Last.Action);
        Assert.AreEqual("850 AP", f.Text("bankroll"));
        await f.Click("bank");
        Assert.AreEqual(1, f.Rounds.Calls.Count, "Animation must block subsequent actions.");
        f.FinishAnimation();
        var round = f.Rounds.Current!;
        await f.Click("bank");
        Assert.AreEqual(round.Id, f.Rounds.Last.SessionId);
        Assert.AreEqual(1L, f.Rounds.Last.ExpectedRevision);
        Assert.AreEqual(CasinoRoundAction.Bank, f.Rounds.Last.Action);
        Assert.AreEqual("0 AP", f.Text("payout"), "Payout is concealed until the animation ends.");
        f.FinishAnimation();
        f.Presenter.Tick();
        Assert.AreEqual("150 AP", f.Text("payout"));
        Assert.AreEqual(1000L, f.Wallet.Balance.Allocated);
        Assert.AreEqual(2, f.Rounds.Receipts.Count);
    }

    [TestMethod]
    public async Task UncertainReceiptBlocksNewActions_AndRetryReusesEntireOperation()
    {
        using var f = await Fixture.Create();
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk);
        f.Wallet.FailRead = true;
        await f.Click("play");
        var operation = f.Rounds.Last!;
        Assert.IsTrue(f.Class("uncertain"));
        await f.Click("play");
        await f.Click("plus");
        Assert.AreEqual(1, f.Rounds.Calls.Count);
        f.Wallet.FailRead = false;
        await f.Click("retry");
        Assert.AreEqual(operation, f.Rounds.Last);
        Assert.AreEqual(1, f.Rounds.Receipts.Count);
        Assert.AreEqual(900L, f.Wallet.Balance.Allocated);
        Assert.IsFalse(f.Class("uncertain"));
    }

    [TestMethod]
    public async Task RejectedStartCanBeRetriedWithNewOperation()
    {
        using var f = await Fixture.Create();
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk);
        f.Rounds.RejectOnce = new CasinoRejectedException("Nicht genügend Automatenguthaben.");
        await f.Click("play");
        var rejected = f.Rounds.Last!;
        Assert.IsFalse(f.Class("uncertain"));
        Assert.AreEqual("Nicht genügend Automatenguthaben.", f.Text("status"));
        await f.Click("play");
        Assert.AreNotEqual(rejected.Id, f.Rounds.Last!.Id);
        Assert.AreEqual(1, f.Rounds.Receipts.Count);
    }

    [TestMethod]
    public async Task ReconnectRestoresActiveGame_AndBackReturnsToLobby()
    {
        using var f = await Fixture.Create();
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk);
        await f.Click("play");
        var active = f.Rounds.Current;
        await f.Players.ConnectAsync(new(Player, "Rejoined", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        Assert.AreEqual(0, f.Hud.VisiblePlayers(CasinoRoundPresenter.HudId).Count);
        Assert.IsTrue(await f.Presenter.OpenAsync(Player, CasinoRoundGame.Blackjack));
        Assert.AreEqual("RISK · DOUBLE OR BANK", f.Text("title"));
        Assert.AreEqual("Gespeicherte Runde fortsetzen", f.Text("status"));
        Assert.AreEqual(active, f.Rounds.Current);
        await f.Click("back");
        Assert.AreEqual(1, f.BackCalls);
        Assert.AreEqual(0, f.Hud.VisiblePlayers(CasinoRoundPresenter.HudId).Count);
        await f.Click("bank");
        Assert.AreEqual(1, f.Rounds.Calls.Count, "A hidden HUD must not accept actions.");
    }

    [TestMethod]
    public async Task RevokedPermissionClosesHud_AndCannotCreateRound()
    {
        using var f = await Fixture.Create();
        f.Permission.Allowed = false;
        Assert.IsFalse(await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk));
        Assert.AreEqual(0, f.BeforeOpenCalls);
        f.Permission.Allowed = true;
        Assert.IsTrue(await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk));
        Assert.AreEqual(1, f.BeforeOpenCalls);
        f.Permission.Allowed = false;
        await f.Click("play");
        Assert.AreEqual(0, f.Rounds.Calls.Count);
        Assert.AreEqual(0, f.Hud.VisiblePlayers(CasinoRoundPresenter.HudId).Count);
    }

    [TestMethod]
    public async Task DelayedOpenCannotShowHudForReplacedPlayerSession()
    {
        using var f = await Fixture.Create();
        var delayed = new TaskCompletionSource<DurableCasinoRoundSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Rounds.DelayedRead = delayed.Task;
        var open = f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk).AsTask();
        await f.Players.ConnectAsync(new(Player, "Rejoined", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        delayed.SetResult(null);
        Assert.IsFalse(await open);
        Assert.AreEqual(0, f.BeforeOpenCalls);
        Assert.AreEqual(0, f.Hud.VisiblePlayers(CasinoRoundPresenter.HudId).Count);
    }

    [TestMethod]
    public async Task CloseDuringBookingDoesNotReleasePendingGuardOrReadAnotherSnapshot()
    {
        using var f = await Fixture.Create();
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk);
        var delayed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Rounds.DelayedApply = delayed.Task;
        var booking = f.Click("play").AsTask();
        try
        {
            await f.Click("close");
            Assert.AreEqual(1, f.Rounds.ReadCalls, "Closing must not start another read while booking is pending.");
            Assert.IsFalse(await f.Presenter.OpenAsync(Player, CasinoRoundGame.Blackjack));
        }
        finally
        {
            delayed.SetResult(true);
            await booking;
        }
        Assert.AreEqual(0, f.Hud.VisiblePlayers(CasinoRoundPresenter.HudId).Count);
    }

    [TestMethod]
    public async Task StakeBoundsAndActiveRoundPreventChangingReservedStake()
    {
        using var f = await Fixture.Create();
        await f.Presenter.OpenAsync(Player, CasinoRoundGame.Risk);
        for (var i = 0; i < 20; i++) await f.Click("minus");
        Assert.AreEqual("50 AP", f.Text("stake"));
        for (var i = 0; i < 20; i++) await f.Click("plus");
        Assert.AreEqual("500 AP", f.Text("stake"));
        await f.Click("play");
        f.FinishAnimation();
        await f.Click("minus");
        Assert.AreEqual("500 AP", f.Text("stake"));
        Assert.IsFalse(await f.Presenter.OpenAsync(Player, (CasinoRoundGame)99));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance() => _now = _now.AddSeconds(4);
        public void AdvanceMilliseconds(int milliseconds) => _now = _now.AddMilliseconds(milliseconds);
    }

    private sealed class Permission : IPermissionEvaluator
    {
        public bool Allowed = true;
        public ValueTask<bool> HasPermissionAsync(PlayerId player, PermissionId permission, CancellationToken token = default)
            => ValueTask.FromResult(Allowed);
    }

    private sealed class Wallet : ICasinoWallet
    {
        public CasinoBalance Balance = new(2000, 1000, 0);
        public bool FailRead;
        public ValueTask<CasinoBalance> ReadAsync(PlayerId player, CancellationToken token = default)
            => FailRead ? ValueTask.FromException<CasinoBalance>(new InvalidOperationException("Connection lost")) : ValueTask.FromResult(Balance);
        public ValueTask<CasinoReceipt> ApplyAsync(CasinoOperation operation, CancellationToken token = default)
            => throw new AssertFailedException("Round presenter must use the durable rounds coordinator for mutations.");
        public ValueTask<IReadOnlyList<CasinoReceipt>> HistoryAsync(PlayerId player, CancellationToken token = default)
            => ValueTask.FromResult<IReadOnlyList<CasinoReceipt>>([]);
    }

    private sealed class Rounds(Wallet wallet) : ICasinoRounds
    {
        public DurableCasinoRoundSnapshot? Current;
        public DurableCasinoRoundSnapshot? NextSnapshot;
        public DurableCasinoRoundOperation? Last;
        public Exception? RejectOnce;
        public Task<DurableCasinoRoundSnapshot?>? DelayedRead;
        public Task? DelayedApply;
        public int ReadCalls;
        public List<DurableCasinoRoundOperation> Calls { get; } = [];
        public Dictionary<Guid, DurableCasinoRoundReceipt> Receipts { get; } = [];
        public ValueTask<DurableCasinoRoundSnapshot?> ReadAsync(PlayerId player, CancellationToken token = default)
        {
            ReadCalls++;
            return DelayedRead is { } delayed ? new(delayed) : ValueTask.FromResult(Current is { IsComplete: false } ? Current : null);
        }
        public async ValueTask<DurableCasinoRoundReceipt> ApplyAsync(DurableCasinoRoundOperation operation, CancellationToken token = default)
        {
            if (DelayedApply is { } delayed) await delayed;
            Last = operation;
            Calls.Add(operation);
            if (RejectOnce is { } rejected) { RejectOnce = null; throw rejected; }
            if (Receipts.TryGetValue(operation.Id, out var replay)) return replay;
            if (NextSnapshot is { } next)
            {
                Current = next;
                var prepared = new DurableCasinoRoundReceipt(operation.Id, wallet.Balance, next);
                Receipts.Add(operation.Id, prepared);
                return prepared;
            }
            var start = operation.Action == CasinoRoundAction.Start;
            var previous = Current is null ? null : JsonSerializer.Deserialize<RiskRoundState>(Current.StateJson);
            var state = start ? RiskRules.Start(operation.Stake) : RiskRules.Bank(previous!, previous!.Revision);
            var complete = state.Status != RiskRoundStatus.Active;
            var payout = state.FinalPayout ?? 0;
            wallet.Balance = wallet.Balance with
            {
                Allocated = wallet.Balance.Allocated - (start ? operation.Stake : 0) + payout,
                Revision = wallet.Balance.Revision + 1,
            };
            Current = new(operation.SessionId, operation.Player, operation.Game, operation.Stake,
                (Current?.Revision ?? 0) + 1, complete, operation.Stake, payout, JsonSerializer.Serialize(state));
            var receipt = new DurableCasinoRoundReceipt(operation.Id, wallet.Balance, Current);
            Receipts.Add(operation.Id, receipt);
            return receipt;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly Clock Clock = new();
        public readonly Permission Permission = new();
        public readonly Wallet Wallet = new();
        public readonly TestCustomHudService Hud = new();
        public readonly PlayerRegistry Players;
        public readonly CasinoRoundPresenter Presenter;
        public readonly Rounds Rounds;
        public int BeforeOpenCalls;
        public int BackCalls;

        private Fixture()
        {
            var events = new AnoEventBus();
            Players = new(events);
            Rounds = new(Wallet);
            Presenter = new(Hud, Players, Permission, Wallet, Rounds, events, _ => BeforeOpenCalls++,
                (_, _) => { BackCalls++; return ValueTask.CompletedTask; }, Clock);
        }

        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            await f.Players.ConnectAsync(new(Player, "Tester", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
            return f;
        }

        public ValueTask Click(string action) => Hud.ClickAsync(Player, CasinoRoundPresenter.HudId, "casino_round_" + action);
        public string? Text(string panel) => Hud.Text(Player, CasinoRoundPresenter.HudId, "casino_round_" + panel);
        public bool Class(string name) => Hud.Class(Player, CasinoRoundPresenter.HudId, "casino_round_root", name);
        public bool PanelClass(string panel, string name) => Hud.Class(Player, CasinoRoundPresenter.HudId, "casino_round_" + panel, name);
        public void FinishAnimation() { Clock.Advance(); Presenter.Tick(); }
        public void Dispose() => Presenter.Dispose();
    }
}
