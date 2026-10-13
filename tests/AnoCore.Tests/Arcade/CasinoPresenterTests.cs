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
    public async Task CoinflipSettlesBeforeAnimation_AndTickCannotAwardAgain()
    {
        using var f = await Fixture.Create(); await f.Click("load"); await f.Click("coinflip"); await f.Click("head"); await f.Click("play");
        Assert.AreEqual("600 AP", f.Text("bank")); Assert.AreEqual("0 AP", f.Text("win"));
        await f.Click("play"); Assert.AreEqual(2, f.Wallet.Operations.Count);
        await f.Click("close"); f.Clock.Advance(); f.Presenter.Tick(); f.Presenter.Tick();
        Assert.AreEqual(600L, f.Wallet.Balance.Allocated);
        await f.Commands.ExecuteAsync("!anocasino", Player); Assert.AreEqual("200 AP", f.Text("win"));
    }
    [TestMethod]
    public async Task SlotsAndRouletteUseServerRequests_AndPermissionsReleaseHud()
    {
        using var f = await Fixture.Create(); await f.Click("load"); await f.Click("roulette"); await f.Click("number"); await f.Click("play");
        Assert.AreEqual(CasinoGame.Roulette, f.Wallet.Last!.Game!.Game); Assert.AreEqual(0, f.Wallet.Last.Game.Choice);
        Assert.AreEqual("4.000 AP", f.Text("bank")); f.Clock.Advance(); f.Presenter.Tick();
        await f.Click("slots"); await f.Click("play"); Assert.AreEqual(CasinoGame.Slots, f.Wallet.Last.Game!.Game);
        f.Permission.Allowed = false; await f.Click("close"); Assert.AreEqual(0, f.Hud.VisiblePlayers(CasinoPresenter.HudId).Count);
    }
    [TestMethod]
    public async Task UncertainReceiptRetriesSameOperation_AndBlocksNewGame()
    {
        using var f = await Fixture.Create(); await f.Click("load"); await f.Click("coinflip"); f.Wallet.FailRead = true; await f.Click("play");
        var operation = f.Wallet.Last!.Id; var count = f.Wallet.Operations.Count;
        await f.Click("play"); Assert.AreEqual(count, f.Wallet.Operations.Count);
        f.Wallet.FailRead = false; await f.Click("retry"); Assert.AreEqual(operation, f.Wallet.Last.Id); Assert.AreEqual(count, f.Wallet.Operations.Count);
        Assert.AreEqual("600 AP", f.Text("bank"));
    }
    [TestMethod]
    public async Task CreditCommandIsConsoleOnly_AndBounded()
    {
        using var f = await Fixture.Create(); Assert.IsFalse((await f.Commands.ExecuteAsync("!anoapcredit 76561198000911901 100", Player)).Success);
        Assert.IsFalse((await f.Commands.ExecuteAsync("!anoapcredit 76561198000911901 1000001", null)).Success);
        Assert.IsTrue((await f.Commands.ExecuteAsync("!anoapcredit 76561198000911901 100", null)).Success);
        Assert.AreEqual(1100L, f.Wallet.Balance.Available);
    }
    [TestMethod]
    public async Task RejectedPlayDoesNotPreventLoadingFundsAfterwards()
    {
        using var f = await Fixture.Create();
        await f.Click("coinflip");
        f.Wallet.RejectOnce = new CasinoRejectedException("Nicht genügend Automatenguthaben.");
        await f.Click("play");
        await f.Click("load");
        Assert.AreEqual("500 AP", f.Text("bank"));
    }

    private sealed class Clock : TimeProvider { private DateTimeOffset _now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => _now; public void Advance() => _now = _now.AddSeconds(4); }
    private sealed class Permission : IPermissionEvaluator { public bool Allowed = true; public ValueTask<bool> HasPermissionAsync(PlayerId p, PermissionId x, CancellationToken t = default) => ValueTask.FromResult(Allowed); }
    private sealed class Wallet : ICasinoWallet
    {
        public CasinoBalance Balance = new(1000, 0, 0); public bool FailRead;
        public Exception? RejectOnce; public CasinoOperation? Last; public Dictionary<Guid, CasinoReceipt> Operations = [];
        public ValueTask<CasinoBalance> ReadAsync(PlayerId p, CancellationToken t = default) => FailRead ? ValueTask.FromException<CasinoBalance>(new InvalidOperationException("Connection lost")) : ValueTask.FromResult(Balance);
        public ValueTask<IReadOnlyList<CasinoReceipt>> HistoryAsync(PlayerId p, CancellationToken t = default) => ValueTask.FromResult<IReadOnlyList<CasinoReceipt>>(Operations.Values.Reverse().ToArray());
        public ValueTask<CasinoReceipt> ApplyAsync(CasinoOperation o, CancellationToken t = default)
        {
            if (RejectOnce is { } rejection) { RejectOnce = null; throw rejection; }
            Last = o; if (Operations.TryGetValue(o.Id, out var old)) return ValueTask.FromResult(old);
            CasinoOutcome? result = null; var a = Balance.Available; var b = Balance.Allocated;
            switch (o.Action) { case CasinoAction.Load: a -= o.Amount; b += o.Amount; break; case CasinoAction.Return: a += b; b = 0; break; case CasinoAction.Credit: a += o.Amount; break; case CasinoAction.Play: result = CasinoRules.Draw(o.Game!, _ => 0); b = b - o.Game!.Stake + result.Payout; break; }
            Balance = new(a, b, Balance.Revision + 1); var receipt = new CasinoReceipt(o.Id, Balance, result); Operations[o.Id] = receipt; return ValueTask.FromResult(receipt);
        }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly Clock Clock = new(); public readonly Permission Permission = new(); public readonly Wallet Wallet = new(); public readonly TestCustomHudService Hud = new();
        public readonly PlayerRegistry Players; public readonly CommandRegistry Commands; public readonly CasinoPresenter Presenter;
        private Fixture() { var events = new AnoEventBus(); Players = new(events); Commands = new(Permission); Presenter = new(Hud, Players, Permission, Wallet, Commands, events, _ => { }, Clock); }
        public static async Task<Fixture> Create() { var f = new Fixture(); await f.Players.ConnectAsync(new(Player, "Tester", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow)); Assert.IsTrue((await f.Commands.ExecuteAsync("!anocasino", Player)).Success); return f; }
        public ValueTask Click(string id) => Hud.ClickAsync(Player, CasinoPresenter.HudId, "casino_" + id);
        public string? Text(string id) => Hud.Text(Player, CasinoPresenter.HudId, "casino_" + id);
        public void Dispose() => Presenter.Dispose();
    }
}
