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

        Assert.AreEqual("600 AP", f.CoinText("bank"));
        Assert.AreEqual("0 AP", f.CoinText("win"));

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

        f.Clock.AdvanceMilliseconds(2600);
        f.Presenter.Tick();
        Assert.IsFalse(f.SlotsClass("reel0", "rolling"));
        Assert.IsTrue(f.SlotsClass("reel0", "settled"));
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
        Assert.AreEqual("4.000 AP", f.RouletteText("bank"));
        Assert.IsTrue(f.RouletteClass("wheel", "spinning"));
        Assert.IsTrue(f.RouletteClass("wheel", "target-n0"));
        Assert.IsFalse(f.RouletteClass("wheel", "turn-b"));
        for (var number = 1; number <= 36; number++)
            Assert.IsFalse(f.RouletteClass("wheel", "target-n" + number));

        f.Clock.AdvanceMilliseconds(3600);
        f.Presenter.Tick();

        Assert.IsFalse(f.RouletteClass("wheel", "spinning"));
        Assert.IsTrue(f.RouletteClass("wheel", "land-green"));
        Assert.IsTrue(f.RouletteClass("wheel", "land-n0"));
        f.Clock.AdvanceMilliseconds(400);
        f.Presenter.Tick();
        await f.ClickRoulette("play");
        Assert.IsTrue(f.RouletteClass("wheel", "spinning"));
        Assert.IsTrue(f.RouletteClass("wheel", "turn-b"), "Consecutive spins must restart the native keyframes.");
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
        Assert.AreEqual("600 AP", f.CoinText("bank"));
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
                    result = CasinoRules.Draw(operation.Game!, _ => 0);
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

        private Fixture()
        {
            var events = new AnoEventBus();
            Players = new(events);
            Commands = new(Permission);
            Presenter = new(
                Hud, Players, Permission, Wallet, Commands, events,
                _ => { }, Clock, reportError: Errors.Add);
        }

        public static async Task<Fixture> Create()
        {
            var fixture = new Fixture();
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
