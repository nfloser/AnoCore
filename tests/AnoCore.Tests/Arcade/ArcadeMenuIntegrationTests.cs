using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Arcade;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Menus;
using AnoCore.Runtime.Players;
using AnoCore.Tests.AnoVeto;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class ArcadeMenuIntegrationTests
{
    private static readonly PlayerId Player = new(76561198000911003);

    [TestMethod]
    public async Task CasinoDestinationHandsOffHomeHud_AndReconcileDoesNotHideCasino()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Tester", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        var permissions = new Permissions();
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var hud = new TestCustomHudService();
        using var panorama = new PanoramaMenuPresenter(hud, menus, players, commands, events);
        using var arcade = new ArcadeProbePresenter(hud, players, permissions, commands, events,
            beforeOpen: id => { menus.Close(id); panorama.Reconcile(); });
        using var home = new AnoHomeMenuModule(commands, players, menus, permissions, events);
        Assert.IsTrue((await commands.ExecuteAsync("!anomenunavigation", Player)).Success);
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out var definition));
        Assert.IsTrue(definition!.Options.Any(option => option.Id == "anoarcadetest" && option.Label == "AnoCasino · Animationstest"));
        Assert.IsTrue(panorama.Open(Player));
        var row = definition.Options.ToList().FindIndex(option => option.Id == "anoarcadetest");
        Assert.IsTrue(row < PanoramaMenuPresenter.PageSize);
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_" + row);
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
        Assert.IsFalse(hud.VisiblePlayers(PanoramaMenuPresenter.HudId).Contains(Player));
        Assert.IsTrue(hud.VisiblePlayers(ArcadeProbePresenter.HudId).Contains(Player));
        panorama.Reconcile();
        Assert.IsTrue(hud.VisiblePlayers(ArcadeProbePresenter.HudId).Contains(Player));
        await hud.ClickAsync(Player, ArcadeProbePresenter.HudId, "ano_arcade_slots");
        await hud.ClickAsync(Player, ArcadeProbePresenter.HudId, "ano_arcade_load");
        Assert.AreEqual("500 Test-AP", hud.Text(Player, ArcadeProbePresenter.HudId, "ano_arcade_bank"));
        await hud.ClickAsync(Player, ArcadeProbePresenter.HudId, "ano_arcade_close");
        Assert.IsFalse(hud.VisiblePlayers(ArcadeProbePresenter.HudId).Contains(Player));
        Assert.IsTrue((await commands.ExecuteAsync("!anomenu", Player)).Success);
        Assert.IsTrue(panorama.Open(Player));
    }

    [TestMethod]
    public async Task DisabledOrUnauthorizedCasinoHasNoNavigationEntry()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Tester", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        var permissions = new Permissions();
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var hud = new TestCustomHudService();
        using var home = new AnoHomeMenuModule(commands, players, menus, permissions, events);
        await commands.ExecuteAsync("!anomenunavigation", Player);
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out var disabled));
        Assert.IsFalse(disabled!.Options.Any(option => option.Id == "anoarcadetest"));
        using var arcade = new ArcadeProbePresenter(hud, players, permissions, commands, events);
        permissions.Allowed = false;
        await commands.ExecuteAsync("!anomenunavigation", Player);
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out var unauthorized));
        Assert.IsFalse(unauthorized!.Options.Any(option => option.Id == "anoarcadetest"));
        Assert.IsFalse((await commands.ExecuteAsync("!anoarcadetest", Player)).Success);
        Assert.AreEqual(0, hud.VisiblePlayers(ArcadeProbePresenter.HudId).Count);
    }

    [TestMethod]
    public async Task CasinoHandoffInvalidatesDelayedDashboardRead()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Tester", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        var permissions = new Permissions();
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var rank = commands.Register(new("test"), new("anorank", "Rank"), async _ =>
        { entered.SetResult(); await release.Task; return CommandResult.Ok("Rank"); });
        using var home = new AnoHomeMenuModule(commands, players, menus, permissions, events);
        var pending = commands.ExecuteAsync("!anomenu", Player).AsTask();
        await entered.Task;
        home.CloseForPlayer(Player);
        release.SetResult();
        await pending;
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
    }

    private sealed class Permissions : IPermissionEvaluator
    {
        public bool Allowed { get; set; } = true;
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Allowed);
    }
}
