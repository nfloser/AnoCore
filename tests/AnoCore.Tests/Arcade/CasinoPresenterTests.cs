using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Arcade.Casino;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Tests.AnoVeto;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class CasinoPresenterTests
{
    private static readonly PlayerId Player = new(76561198000911901);

    [TestMethod]
    public async Task RouletteResetClearsTheLastPoseBeforeReconstructingNativeState()
    {
        using var f = await Fixture.Create();
        await f.ClickLobby("load");
        await f.ClickLobby("roulette");
        f.Wallet.RouletteNumber = 32;
        await f.ClickRoulette("play");
        f.Clock.AdvanceMilliseconds(4000); f.Presenter.Tick();
        var frame = "ball-frame-" + RouletteBallMotion.LandingFrame(32);
        Assert.IsTrue(f.RouletteClass("ball", frame));
        var operations = f.Wallet.Operations.Count;
        f.Presenter.Reset();
        Assert.IsFalse(f.RouletteClass("ball", frame));
        Assert.IsTrue((await f.Commands.ExecuteAsync("!anocasino", Player)).Success);
        await f.ClickLobby("roulette");
        Assert.IsTrue(f.RouletteClass("ball", frame));
        Assert.AreEqual(operations, f.Wallet.Operations.Count);
    }

    [TestMethod]
    public async Task HistoricalRouletteResultRestoresBallWithoutInventingAnUnknownNetStake()
    {
        using var f = await Fixture.Create();
        var id = Guid.NewGuid();
        f.Wallet.Balance = new(1000, 3600000, 1);
        f.Wallet.Operations[id] = new(id, f.Wallet.Balance,
            CasinoRules.EvaluateNumber(new(CasinoGame.Roulette, 100000, 32), 32));
        Assert.IsTrue((await f.Commands.ExecuteAsync("!anocasino", Player)).Success);
        await f.ClickLobby("roulette");
        Assert.AreEqual("32", f.RouletteText("wheelresult"));
        Assert.IsTrue(f.RouletteClass("ball", "ball-frame-" + RouletteBallMotion.LandingFrame(32)));
        Assert.IsTrue(f.RouletteClass("wheel", "land-red"));
        Assert.IsFalse(f.RouletteText("result")!.Contains("Netto"));
        Assert.AreEqual(1, f.Wallet.Operations.Count);
    }

    [TestMethod]
    public async Task ClosingDuringRouletteAndRefreshingDoesNotReplayOrChargeAgain()
    {
        using var f = await Fixture.Create();
        await f.ClickLobby("load");
        await f.ClickLobby("roulette");
        await f.ClickRoulette("r0");
        await f.ClickRoulette("play");
        var operations = f.Wallet.Operations.Count;
        var balance = f.Wallet.Balance;
        await f.ClickRoulette("close");
        f.Clock.AdvanceMilliseconds(4000); f.Presenter.Tick();
        Assert.IsTrue((await f.Commands.ExecuteAsync("!anocasino", Player)).Success);
        await f.ClickLobby("roulette");
        await f.ClickRoulette("refresh");
        Assert.AreEqual(operations, f.Wallet.Operations.Count);
        Assert.AreEqual(balance, f.Wallet.Balance);
        Assert.AreEqual("0", f.RouletteText("wheelresult"));
        Assert.IsTrue(f.RouletteClass("ball", "ball-frame-" + RouletteBallMotion.LandingFrame(0)));
        Assert.IsTrue(f.RouletteText("result")!.Contains("Netto +3,5 Tsd. €"));
    }

    [TestMethod]
    public async Task RouletteMovesAndLandsOnAllNumbersWithoutRebookingAndRetainsOriginalNetStake()
    {
        using var f = await Fixture.Create();
        f.Wallet.Balance = new(1000, 100000, 0);
        await f.ClickLobby("refresh");
        await f.ClickLobby("roulette");
        for (var number = 0; number <= 36; number++)
        {
            f.Wallet.RouletteNumber = number;
            await f.ClickRoulette("r" + number);
            Assert.IsTrue(f.RouletteClass("r" + number, "selected"));
            await f.ClickRoulette("play");
            var operations = f.Wallet.Operations.Count;
            Assert.AreEqual("…", f.RouletteText("wheelresult"));
            for (var step = 0; step < 72; step++)
            {
                f.Clock.AdvanceMilliseconds(50);
                f.Presenter.Tick();
            }
            Assert.IsTrue(f.RouletteClass("ball", "ball-frame-" + RouletteBallMotion.LandingFrame(number)));
            Assert.AreEqual(number.ToString(), f.RouletteText("wheelresult"));
            f.Clock.AdvanceMilliseconds(400); f.Presenter.Tick();
            Assert.AreEqual(operations, f.Wallet.Operations.Count);
        }
        var net = f.RouletteText("result");
        await f.ClickRoulette("plus");
        Assert.AreEqual(net, f.RouletteText("result"));
        Assert.IsFalse(f.RouletteText("status")!.Contains("AP"));
    }

    [TestMethod]
    public async Task RouletteQuickStakesRespectConfiguredMaximumAndAvailableFunds()
    {
        using var f = await Fixture.Create(new() { Roulette = 100000 });
        f.Wallet.Balance = new(1000, 125000, 0);
        await f.ClickLobby("refresh");
        await f.ClickLobby("roulette");
        await f.ClickRoulette("betmax");
        Assert.AreEqual("100.000 €", f.RouletteText("stake"));
        await f.ClickRoulette("bethalf");
        Assert.AreEqual("50.000 €", f.RouletteText("stake"));
        await f.ClickRoulette("betdouble");
        Assert.AreEqual("100.000 €", f.RouletteText("stake"));
        await f.ClickRoulette("betmin");
        Assert.AreEqual("50 €", f.RouletteText("stake"));
        Assert.AreEqual(0, f.Wallet.Operations.Count);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task CoinFacesHoldBetweenSwapsAndDoNotRevealBookedResultEarly(int number)
    {
        using var f = await Fixture.Create();
        f.Wallet.CoinNumber = number;
        await f.ClickLobby("load"); await f.ClickLobby("coinflip"); await f.ClickCoin("play");
        bool Face(string name) => f.Hud.Class(Player, CasinoPresenter.CoinflipHudId, "casino_coinflip_coin", name);
        Assert.IsTrue(Face("coin-armed"));
        Assert.IsTrue(Face("show-heads"));
        Assert.IsFalse(Face("flipping"));
        f.Clock.AdvanceMilliseconds(150); f.Presenter.Tick();
        Assert.IsTrue(Face("coin-armed"));
        f.Clock.AdvanceMilliseconds(100); f.Presenter.Tick();
        Assert.IsTrue(Face("flipping"));
        Assert.IsFalse(Face("coin-armed"));
        Assert.IsTrue(Face("show-heads"));
        f.Clock.AdvanceMilliseconds(200); f.Presenter.Tick();
        Assert.IsTrue(Face("show-heads"), "The face needs a visible hold, not an immediate swap.");
        f.Clock.AdvanceMilliseconds(100); f.Presenter.Tick();
        Assert.IsTrue(Face("coin-turning"));
        Assert.IsTrue(Face("show-heads"));
        f.Clock.AdvanceMilliseconds(100); f.Presenter.Tick();
        Assert.IsTrue(Face("show-tails"));
        Assert.IsFalse(Face("coin-turning"));
        f.Clock.AdvanceMilliseconds(250); f.Presenter.Tick();
        Assert.IsTrue(Face("show-tails"));
        Assert.AreEqual("…", f.CoinText("result"));
        await f.ClickCoin("play");
        Assert.AreEqual(2, f.Wallet.Operations.Count);
        f.Clock.AdvanceMilliseconds(2700); f.Presenter.Tick();
        Assert.AreEqual(number == 0 ? "KOPF" : "ZAHL", f.CoinText("result"));
        Assert.IsTrue(Face(number == 0 ? "show-heads" : "show-tails"));
        Assert.IsFalse(Face("coin-turning"));
        Assert.AreEqual(2, f.Wallet.Operations.Count);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task CoinFacesAnimateBeforeBookedLandingAndNextThrowRestarts(int number)
    {
        using var f = await Fixture.Create();
        f.Wallet.CoinNumber = number;
        await f.ClickLobby("load"); await f.ClickLobby("coinflip"); await f.ClickCoin("play");
        Assert.AreEqual("…", f.CoinText("result"));
        Assert.IsTrue(f.Hud.Class(Player, CasinoPresenter.CoinflipHudId, "casino_coinflip_coin", "coin-armed"));
        Assert.IsFalse(f.Hud.Class(Player, CasinoPresenter.CoinflipHudId, "casino_coinflip_coin", "flipping"));
        f.Clock.AdvanceMilliseconds(1000); f.Presenter.Tick();
        Assert.AreEqual("…", f.CoinText("result"));
        Assert.AreEqual(2, f.Wallet.Operations.Count);
        f.Clock.AdvanceMilliseconds(2600); f.Presenter.Tick();
        Assert.AreEqual(number == 0 ? "KOPF" : "ZAHL", f.CoinText("result"));
        Assert.IsFalse(f.Hud.Class(Player, CasinoPresenter.CoinflipHudId, "casino_coinflip_coin", "flipping"));
        Assert.IsTrue(f.Hud.Class(Player, CasinoPresenter.CoinflipHudId, "casino_coinflip_coin", number == 0 ? "land-heads" : "land-tails"));
        f.Clock.AdvanceMilliseconds(400); f.Presenter.Tick();
        await f.ClickCoin("play");
        Assert.AreEqual("…", f.CoinText("result"));
        Assert.IsFalse(f.Hud.Class(Player, CasinoPresenter.CoinflipHudId, "casino_coinflip_coin", "land-heads"));
        Assert.AreEqual(3, f.Wallet.Operations.Count);
    }

    [TestMethod]
    public async Task ImmediateGameControlsRespectIndependentLimitsWhenSwitching()
    {
        using var f = await Fixture.Create(new() { Coinflip = 1000, Roulette = 50 });
        await f.ClickLobby("coinflip");
        for (var n = 0; n < 30; n++) await f.ClickCoin("plus");
        Assert.AreEqual("1.000 €", f.CoinText("stake"));
        await f.ClickCoin("back"); await f.ClickLobby("roulette");
        Assert.AreEqual("50 €", f.RouletteText("stake"));
        await f.ClickRoulette("plus");
        Assert.AreEqual("50 €", f.RouletteText("stake"));
    }

    [TestMethod]
    public async Task OpenUsesDedicatedLobby_AndGameSelectionSwapsHud()
    {
        using var f = await Fixture.Create();

        Assert.IsTrue(f.Hud.VisiblePlayers(CasinoPresenter.LobbyHudId).Contains(Player));
        Assert.IsFalse(f.Hud.VisiblePlayers(CasinoPresenter.SlotsHudId).Contains(Player));

        await f.ClickLobby("slots");

        Assert.IsFalse(f.Hud.VisiblePlayers(CasinoPresenter.LobbyHudId).Contains(Player));
        Assert.IsTrue(f.Hud.VisiblePlayers(CasinoPresenter.SlotsHudId).Contains(Player));

        await f.ClickSlots("back");

        Assert.IsTrue(f.Hud.VisiblePlayers(CasinoPresenter.LobbyHudId).Contains(Player));
        Assert.IsFalse(f.Hud.VisiblePlayers(CasinoPresenter.SlotsHudId).Contains(Player));
    }

    [TestMethod]
    public async Task CoinflipSettlesBeforeAnimation_AndTickCannotAwardAgain()
    {
        using var f = await Fixture.Create();
        await f.ClickLobby("load");
        await f.ClickLobby("coinflip");
        await f.ClickCoin("head");
        await f.ClickCoin("play");

        Assert.AreEqual("600 €", f.CoinText("bank"));
        Assert.AreEqual("0 €", f.CoinText("win"));

        await f.ClickCoin("play");
        Assert.AreEqual(2, f.Wallet.Operations.Count);

        await f.ClickCoin("close");
        f.Clock.Advance();
        f.Presenter.Tick();
        f.Presenter.Tick();

        Assert.AreEqual(600L, f.Wallet.Balance.Allocated);
        await f.Commands.ExecuteAsync("!anocasino", Player);
        Assert.AreEqual(200L, f.LobbyLastWinFromStatus());
    }

    [TestMethod]
    public async Task SlotsReelsStopAtMilestonesAndWinningLinesClearOnNextSpin()
    {
        using var f = await Fixture.Create();
        await f.ClickLobby("load");
        await f.ClickLobby("slots");
        await f.ClickSlots("play");

        for (var i = 0; i < 3; i++)
            Assert.IsTrue(f.SlotsClass("reel" + i, "rolling"));

        f.Clock.AdvanceMilliseconds(2150);
        f.Presenter.Tick();
        Assert.IsTrue(f.SlotsClass("reel0", "settling"));
        Assert.IsFalse(f.SlotsClass("reel0", "rolling"));
        Assert.IsFalse(f.SlotsClass("reel0", "settled"));
        Assert.IsTrue(f.SlotsClass("reel1", "rolling"));
        await f.ClickSlots("play");
        Assert.IsTrue(f.SlotsClass("reel0", "settling"), "Settling cannot start another booking.");

        f.Clock.AdvanceMilliseconds(450);
        f.Presenter.Tick();
        Assert.IsFalse(f.SlotsClass("reel0", "rolling"));
        Assert.IsTrue(f.SlotsClass("reel0", "settled"));
        Assert.IsFalse(f.SlotsClass("reel0", "settling"));
        Assert.IsTrue(f.SlotsClass("reel1", "rolling"));

        f.Clock.AdvanceMilliseconds(500);
        f.Presenter.Tick();
        Assert.IsFalse(f.SlotsClass("reel1", "rolling"));
        Assert.IsTrue(f.SlotsClass("reel1", "settled"));

        f.Clock.AdvanceMilliseconds(500);
        f.Presenter.Tick();
        Assert.IsFalse(f.SlotsClass("reel2", "rolling"));
        Assert.IsTrue(f.SlotsClass("reel2", "settled"));

        f.Clock.AdvanceMilliseconds(400);
        f.Presenter.Tick();
        for (var i = 0; i < 5; i++)
        {
            Assert.IsTrue(f.SlotsClass("line" + i, "revealed"));
            Assert.IsTrue(f.SlotsClass("line" + i, "active"));
        }

        await f.ClickSlots("play");
        for (var i = 0; i < 5; i++)
        {
            Assert.IsFalse(f.SlotsClass("line" + i, "revealed"));
            Assert.IsFalse(f.SlotsClass("line" + i, "active"));
        }
    }

    [TestMethod]
    public async Task RouletteUsesDedicatedHudAndBookedLanding()
    {
        using var f = await Fixture.Create();
        await f.ClickLobby("load");
        await f.ClickLobby("roulette");
        await f.ClickRoulette("number");
        await f.ClickRoulette("play");

        Assert.AreEqual(CasinoGame.Roulette, f.Wallet.Last!.Game!.Game);
        Assert.AreEqual(0, f.Wallet.Last.Game.Choice);
        Assert.AreEqual("400 €", f.RouletteText("bank"), "Do not reveal a booked payout before the ball lands.");
        Assert.IsTrue(f.RouletteClass("wheel", "spinning"));
        Assert.AreEqual("…", f.RouletteText("wheelresult"));
        Assert.IsTrue(f.RouletteClass("ball", "ball-frame-0"));
        var bookedOperations = f.Wallet.Operations.Count;
        f.Clock.AdvanceMilliseconds(1000);
        f.Presenter.Tick();
        Assert.IsTrue(f.RouletteClass("ball", "ball-frame-" + RouletteBallMotion.FrameAt(0, TimeSpan.FromMilliseconds(1000))));
        Assert.AreEqual(bookedOperations, f.Wallet.Operations.Count);

        f.Clock.AdvanceMilliseconds(2600);
        f.Presenter.Tick();

        Assert.IsFalse(f.RouletteClass("wheel", "spinning"));
        Assert.IsTrue(f.RouletteClass("wheel", "land-green"));
        Assert.IsTrue(f.RouletteClass("wheel", "land-n0"));
        Assert.IsTrue(f.RouletteClass("ball", "ball-frame-" + RouletteBallMotion.LandingFrame(0)));
        Assert.AreEqual("0", f.RouletteText("wheelresult"));
        f.Clock.AdvanceMilliseconds(400);
        f.Presenter.Tick();
        await f.ClickRoulette("play");
        Assert.IsTrue(f.RouletteClass("wheel", "spinning"));
        Assert.IsTrue(f.RouletteClass("ball", "ball-frame-0"));
        Assert.IsFalse(f.RouletteClass("ball", "ball-frame-" + RouletteBallMotion.LandingFrame(0)));
        Assert.AreEqual("…", f.RouletteText("wheelresult"));
    }

    [TestMethod]
    public async Task UncertainReceiptRetriesSameOperation_AndBlocksNewGame()
    {
        using var f = await Fixture.Create();
        await f.ClickLobby("load");
        await f.ClickLobby("coinflip");
        f.Wallet.FailRead = true;
        await f.ClickCoin("play");

        var operation = f.Wallet.Last!.Id;
        var count = f.Wallet.Operations.Count;
        await f.ClickCoin("play");
        Assert.AreEqual(count, f.Wallet.Operations.Count);

        f.Wallet.FailRead = false;
        await f.ClickCoin("retry");
        Assert.AreEqual(operation, f.Wallet.Last.Id);
        Assert.AreEqual(count, f.Wallet.Operations.Count);
        Assert.AreEqual("600 €", f.CoinText("bank"));
    }

    [TestMethod]
    public async Task PermissionFailureClosesAllCasinoViews()
    {
        using var f = await Fixture.Create();
        await f.ClickLobby("slots");
        Assert.IsTrue(f.Hud.VisiblePlayers(CasinoPresenter.SlotsHudId).Contains(Player));

        f.Permission.Allowed = false;
        await f.ClickSlots("rules");

        Assert.IsFalse(f.Hud.VisiblePlayers(CasinoPresenter.LobbyHudId).Contains(Player));
        Assert.IsFalse(f.Hud.VisiblePlayers(CasinoPresenter.SlotsHudId).Contains(Player));
        Assert.IsFalse(f.Hud.VisiblePlayers(CasinoPresenter.CoinflipHudId).Contains(Player));
        Assert.IsFalse(f.Hud.VisiblePlayers(CasinoPresenter.RouletteHudId).Contains(Player));
    }

    [TestMethod]
    public async Task CreditCommandIsConsoleOnly_AndBounded()
    {
        using var f = await Fixture.Create();

        Assert.IsFalse((await f.Commands.ExecuteAsync(
            "!anoapcredit 76561198000911901 100", Player)).Success);
        Assert.IsFalse((await f.Commands.ExecuteAsync(
            "!anoapcredit 76561198000911901 1000001", null)).Success);
        Assert.IsTrue((await f.Commands.ExecuteAsync(
            "!anoapcredit 76561198000911901 100", null)).Success);
        Assert.AreEqual(1100L, f.Wallet.Balance.Available);
    }

    [TestMethod]
    public async Task StorageFailuresReturnCasinoSpecificMessageAndReachServerErrorSink()
    {
        using var f = await Fixture.Create();

        f.Wallet.FailRead = true;
        var open = await f.Commands.ExecuteAsync("!anocasino", Player);
        Assert.IsFalse(open.Success);
        StringAssert.Contains(open.Message, "Casino-Speicher");
        Assert.AreEqual(1, f.Errors.Count);

        f.Wallet.FailRead = false;
        f.Wallet.FailApply = new InvalidOperationException("casino database unavailable");
        var credit = await f.Commands.ExecuteAsync(
            "!anoapcredit 76561198000911901 100", null);
        Assert.IsFalse(credit.Success);
        StringAssert.Contains(credit.Message, "Casino-Speicher");
        Assert.AreEqual(2, f.Errors.Count);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance() => AdvanceMilliseconds(4000);
        public void AdvanceMilliseconds(int milliseconds) => _now = _now.AddMilliseconds(milliseconds);
    }

    private sealed class Permission : IPermissionEvaluator
    {
        public bool Allowed = true;
        public ValueTask<bool> HasPermissionAsync(
            PlayerId player, PermissionId permission, CancellationToken token = default)
            => ValueTask.FromResult(Allowed);
    }

    private sealed class Wallet : ICasinoWallet
    {
        public int CoinNumber;
        public int RouletteNumber;
        public CasinoBalance Balance = new(1000, 0, 0);
        public bool FailRead;
        public Exception? RejectOnce;
        public Exception? FailApply;
        public CasinoOperation? Last;
        public Dictionary<Guid, CasinoReceipt> Operations = [];

        public ValueTask<CasinoBalance> ReadAsync(PlayerId player, CancellationToken token = default)
            => FailRead
                ? ValueTask.FromException<CasinoBalance>(new InvalidOperationException("Connection lost"))
                : ValueTask.FromResult(Balance);

        public ValueTask<IReadOnlyList<CasinoReceipt>> HistoryAsync(
            PlayerId player, CancellationToken token = default)
            => ValueTask.FromResult<IReadOnlyList<CasinoReceipt>>(
                Operations.Values.Reverse().ToArray());

        public ValueTask<CasinoReceipt> ApplyAsync(CasinoOperation operation, CancellationToken token = default)
        {
            if (FailApply is { } failure)
            {
                FailApply = null;
                throw failure;
            }

            if (RejectOnce is { } rejection)
            {
                RejectOnce = null;
                throw rejection;
            }

            Last = operation;
            if (Operations.TryGetValue(operation.Id, out var old))
                return ValueTask.FromResult(old);

            CasinoOutcome? result = null;
            var available = Balance.Available;
            var allocated = Balance.Allocated;

            switch (operation.Action)
            {
                case CasinoAction.Load:
                    available -= operation.Amount;
                    allocated += operation.Amount;
                    break;
                case CasinoAction.Return:
                    available += allocated;
                    allocated = 0;
                    break;
                case CasinoAction.Credit:
                    available += operation.Amount;
                    break;
                case CasinoAction.Play:
                    result = CasinoRules.Draw(operation.Game!, max => max == 2 ? CoinNumber : max == 37 ? RouletteNumber : 0);
                    allocated = allocated - operation.Game!.Stake + result.Payout;
                    break;
            }

            Balance = new(available, allocated, Balance.Revision + 1);
            var receipt = new CasinoReceipt(operation.Id, Balance, result);
            Operations[operation.Id] = receipt;
            return ValueTask.FromResult(receipt);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly Clock Clock = new();
        public readonly Permission Permission = new();
        public readonly Wallet Wallet = new();
        public readonly TestCustomHudService Hud = new();
        public readonly List<Exception> Errors = [];
        public readonly PlayerRegistry Players;
        public readonly CommandRegistry Commands;
        public readonly CasinoPresenter Presenter;

        private Fixture(CasinoStakeLimits? stakeLimits = null)
        {
            var events = new AnoEventBus();
            Players = new(events);
            Commands = new(Permission);
            Presenter = new(
                Hud, Players, Permission, Wallet, Commands, events,
                _ => { }, Clock, reportError: Errors.Add, stakeLimits: stakeLimits);
        }

        public static async Task<Fixture> Create(CasinoStakeLimits? stakeLimits = null)
        {
            var fixture = new Fixture(stakeLimits);
            await fixture.Players.ConnectAsync(
                new(Player, "Tester", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
            Assert.IsTrue((await fixture.Commands.ExecuteAsync("!anocasino", Player)).Success);
            return fixture;
        }

        public ValueTask ClickLobby(string action)
            => Hud.ClickAsync(Player, CasinoPresenter.LobbyHudId, "casino_lobby_" + action);
        public ValueTask ClickSlots(string action)
            => Hud.ClickAsync(Player, CasinoPresenter.SlotsHudId, "casino_slots_" + action);
        public ValueTask ClickCoin(string action)
            => Hud.ClickAsync(Player, CasinoPresenter.CoinflipHudId, "casino_coinflip_" + action);
        public ValueTask ClickRoulette(string action)
            => Hud.ClickAsync(Player, CasinoPresenter.RouletteHudId, "casino_roulette_" + action);

        public string? CoinText(string id)
            => Hud.Text(Player, CasinoPresenter.CoinflipHudId, "casino_coinflip_" + id);
        public string? RouletteText(string id)
            => Hud.Text(Player, CasinoPresenter.RouletteHudId, "casino_roulette_" + id);
        public bool SlotsClass(string id, string name)
            => Hud.Class(Player, CasinoPresenter.SlotsHudId, "casino_slots_" + id, name);
        public bool RouletteClass(string id, string name)
            => Hud.Class(Player, CasinoPresenter.RouletteHudId, "casino_roulette_" + id, name);

        public long LobbyLastWinFromStatus()
        {
            var status = Hud.Text(Player, CasinoPresenter.LobbyHudId, "casino_lobby_status") ?? "";
            return status.Contains("200", StringComparison.Ordinal) ? 200 : 0;
        }

        public void Dispose() => Presenter.Dispose();
    }
}
