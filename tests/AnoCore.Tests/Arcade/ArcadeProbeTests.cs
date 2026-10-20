using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Modules.Arcade;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Tests.AnoVeto;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class ArcadeProbeTests
{
    private static readonly PlayerId Player = new(76561198000911001);

    [TestMethod]
    public async Task TransfersConserveTestCredits_AndStakeIsBounded()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.Click("slots");
        await fixture.Click("load");
        Assert.AreEqual("2.250 Test-AP", fixture.Text("account"));
        Assert.AreEqual("500 Test-AP", fixture.Text("bank"));
        for (var i = 0; i < 30; i++) await fixture.Click("minus");
        Assert.AreEqual("50 Test-AP", fixture.Text("stake"));
        for (var i = 0; i < 30; i++) await fixture.Click("plus");
        Assert.AreEqual("500 Test-AP", fixture.Text("stake"));
        await fixture.Click("return");
        Assert.AreEqual("2.750 Test-AP", fixture.Text("account"));
        Assert.AreEqual("0 Test-AP", fixture.Text("bank"));
    }

    [TestMethod]
    public async Task SpinRequiresBank_BlocksDuplicateSpend_AndRestartsWithAlternateName()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.Click("slots");
        await fixture.Click("spin");
        Assert.IsFalse(fixture.Class("root", "busy"));
        await fixture.Click("load");
        await fixture.Click("spin");
        await fixture.Click("spin");
        await fixture.Click("return");
        await fixture.Click("plus");
        Assert.AreEqual("400 Test-AP", fixture.Text("bank"));
        Assert.AreEqual("100 Test-AP", fixture.Text("stake"));
        Assert.IsTrue(fixture.Class("reel_0", "spin-a"));
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.Presenter.Tick();
        StringAssert.Contains(fixture.Text("status")!, "Textupdate");
        Assert.IsTrue(fixture.Class("reel_0", "spin-a"));
        fixture.Clock.Advance(TimeSpan.FromSeconds(3));
        fixture.Presenter.Tick();
        Assert.AreEqual("0 Test-AP", fixture.Text("win"));
        Assert.IsFalse(fixture.Class("root", "busy"));
        await fixture.Click("spin");
        Assert.IsTrue(fixture.Class("reel_0", "spin-b"));
        Assert.IsFalse(fixture.Class("reel_0", "spin-a"));
        fixture.Clock.Advance(TimeSpan.FromSeconds(4));
        fixture.Presenter.Tick();
        Assert.AreEqual("200 Test-AP", fixture.Text("win"));
        Assert.AreEqual("500 Test-AP", fixture.Text("bank"));
        fixture.Presenter.Tick();
        Assert.AreEqual("500 Test-AP", fixture.Text("bank"));
    }

    [TestMethod]
    public async Task CloseAndReopenPreserveRunningSpin_ResetAndPermissionRevocationReleaseHud()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.Click("slots");
        await fixture.Click("load");
        await fixture.Click("spin");
        await fixture.Click("close");
        fixture.Clock.Advance(TimeSpan.FromSeconds(4));
        fixture.Presenter.Tick();
        await fixture.Commands.ExecuteAsync("!anoarcadetest", Player);
        Assert.AreEqual("400 Test-AP", fixture.Text("bank"));
        fixture.Permission.Allowed = false;
        await fixture.Click("load");
        Assert.AreEqual(0, fixture.Hud.VisiblePlayers(ArcadeProbePresenter.HudId).Count);
        fixture.Permission.Allowed = true;
        await fixture.Commands.ExecuteAsync("!anoarcadetest", Player);
        fixture.Presenter.Reset();
        Assert.AreEqual(0, fixture.Hud.VisiblePlayers(ArcadeProbePresenter.HudId).Count);
    }

    [TestMethod]
    public async Task PlayersHaveIndependentCredits_AndReconnectCannotReuseOldSession()
    {
        using var fixture = await Fixture.CreateAsync();
        var second = new PlayerId(76561198000911002);
        await fixture.Players.ConnectAsync(new(second, "Second", PlayerTeam.CounterTerrorist, true, DateTimeOffset.UtcNow));
        await fixture.Commands.ExecuteAsync("!anoarcadetest", second);
        await fixture.Click("load");
        Assert.AreEqual("2.750 Test-AP", fixture.Hud.Text(second, ArcadeProbePresenter.HudId, "ano_arcade_account"));
        await fixture.Players.ConnectAsync(new(Player, "Rejoined", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        Assert.IsFalse(fixture.Hud.VisiblePlayers(ArcadeProbePresenter.HudId).Contains(Player));
        await fixture.Click("load");
        await fixture.Commands.ExecuteAsync("!anoarcadetest", Player);
        Assert.AreEqual("2.750 Test-AP", fixture.Text("account"));
        Assert.AreEqual("0 Test-AP", fixture.Text("bank"));
    }

    [TestMethod]
    public async Task ManualTextUpdatesPreserveMotion_AndReelsStopInOrder()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.Click("slots");
        await fixture.Click("load");
        await fixture.Click("spin");
        for (var i = 0; i < 10; i++) await fixture.Click("refresh");
        Assert.IsTrue(fixture.Class("reel_0", "spin-a"));
        Assert.IsTrue(fixture.Class("reel_1", "spin-a"));
        Assert.IsTrue(fixture.Class("reel_2", "spin-a"));
        Assert.AreEqual("400 Test-AP", fixture.Text("bank"));
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(2600));
        fixture.Presenter.Tick();
        Assert.IsFalse(fixture.Class("reel_0", "spin-a"));
        Assert.IsTrue(fixture.Class("reel_0", "stop-0"));
        Assert.IsTrue(fixture.Class("reel_1", "spin-a"));
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(500));
        fixture.Presenter.Tick();
        Assert.IsTrue(fixture.Class("reel_1", "stop-1"));
        Assert.IsTrue(fixture.Class("reel_2", "spin-a"));
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(500));
        fixture.Presenter.Tick();
        Assert.IsTrue(fixture.Class("reel_2", "stop-2"));
        Assert.IsTrue(fixture.Class("root", "busy"));
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(400));
        fixture.Presenter.Tick();
        Assert.IsFalse(fixture.Class("root", "busy"));
    }

    [TestMethod]
    public async Task DelayedDisconnectCannotEraseReconnectedPlayersCredits()
    {
        using var fixture = await Fixture.CreateAsync();
        Assert.IsTrue(fixture.Players.TryGet(Player, out var previous));
        await fixture.Players.ConnectAsync(new(Player, "Rejoined", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        await fixture.Commands.ExecuteAsync("!anoarcadetest", Player);
        await fixture.Click("load");
        await fixture.Bus.PublishAsync(new PlayerDisconnectedEvent(previous!));
        Assert.IsTrue(fixture.Hud.VisiblePlayers(ArcadeProbePresenter.HudId).Contains(Player));
        Assert.AreEqual("2.250 Test-AP", fixture.Text("account"));
        Assert.AreEqual("500 Test-AP", fixture.Text("bank"));
    }

    [TestMethod]
    public async Task DelayedPermissionFromOldClickCannotSpendNewSessionCredits()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.Click("slots");
        var permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Permission.Pending = permission.Task;
        var oldClick = fixture.Click("load").AsTask();
        await fixture.Players.ConnectAsync(new(Player, "Rejoined", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        await fixture.Commands.ExecuteAsync("!anoarcadetest", Player);
        permission.SetResult(true);
        await oldClick;
        Assert.AreEqual("2.750 Test-AP", fixture.Text("account"));
        Assert.AreEqual("0 Test-AP", fixture.Text("bank"));
    }

    [TestMethod]
    public async Task TenConsecutiveSpinsSettleOnce_AndResetCurrentWin()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.Click("slots");
        await fixture.Click("load");
        for (var spin = 1; spin <= 10; spin++)
        {
            await fixture.Click("spin");
            Assert.AreEqual("0 Test-AP", fixture.Text("win"));
            Assert.IsTrue(fixture.Class("reel_0", spin % 2 == 1 ? "spin-a" : "spin-b"));
            fixture.Clock.Advance(TimeSpan.FromSeconds(4));
            fixture.Presenter.Tick();
            var bank = fixture.Text("bank");
            fixture.Presenter.Tick();
            Assert.AreEqual(bank, fixture.Text("bank"));
        }
        Assert.AreEqual("500 Test-AP", fixture.Text("bank"));
        Assert.AreEqual("200 Test-AP", fixture.Text("win"));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan time) => _now += time;
    }

    private sealed class Permission : IPermissionEvaluator
    {
        public bool Allowed { get; set; } = true;
        public Task<bool>? Pending { get; set; }
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission,
            CancellationToken cancellationToken = default)
        {
            var pending = Pending;
            Pending = null;
            return pending is null ? ValueTask.FromResult(Allowed) : new ValueTask<bool>(pending);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public AnoEventBus Bus { get; } = new();
        public Clock Clock { get; } = new();
        public Permission Permission { get; } = new();
        public TestCustomHudService Hud { get; } = new();
        public PlayerRegistry Players { get; }
        public CommandRegistry Commands { get; }
        public ArcadeProbePresenter Presenter { get; }

        private Fixture()
        {
            var bus = Bus;
            Players = new PlayerRegistry(bus);
            Commands = new CommandRegistry(Permission);
            Presenter = new ArcadeProbePresenter(Hud, Players, Permission, Commands, bus, Clock);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture.Players.ConnectAsync(new(Player, "Viewer", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
            Assert.IsTrue((await fixture.Commands.ExecuteAsync("!anoarcadetest", Player)).Success);
            return fixture;
        }

        public ValueTask Click(string button) => Hud.ClickAsync(Player, ArcadeProbePresenter.HudId, "ano_arcade_" + button);
        public string? Text(string panel) => Hud.Text(Player, ArcadeProbePresenter.HudId, "ano_arcade_" + panel);
        public bool Class(string panel, string name) => Hud.Class(Player, ArcadeProbePresenter.HudId, "ano_arcade_" + panel, name);
        public void Dispose() => Presenter.Dispose();
    }
}
