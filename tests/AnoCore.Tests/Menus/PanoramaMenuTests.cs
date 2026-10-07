using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Menus;
using AnoCore.Runtime.Players;
using AnoCore.Tests.AnoVeto;

namespace AnoCore.Tests.Menus;

[TestClass]
public sealed class PanoramaMenuTests
{
    private static readonly PlayerId Player = new(76561198000295101);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-07T16:00:00Z");

    [TestMethod]
    public async Task PagesAreBoundedPerPlayerAndLabelsRemainPlainText()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var other = new PlayerId(76561198000295102);
        await players.ConnectAsync(new(other, "Other", PlayerTeam.CounterTerrorist, true, Now));
        var menus = new MenuService();
        var hud = new TestCustomHudService();
        var selected = -1;
        var options = Enumerable.Range(0, 8).Select(index => new MenuOption("item" + index,
            "&lt;b&gt;Row " + index + "&lt;/b&gt;", _ => { selected = index; return ValueTask.CompletedTask; }, keepOpen: true)).ToArray();
        using var registration = menus.Register(new("test"), new(new("ano.test.menu"), "Test", options));
        menus.Open(Player, new("ano.test.menu"));
        menus.Open(other, new("ano.test.menu"));
        using var presenter = new PanoramaMenuPresenter(hud, menus, players, new CommandRegistry(new Permissions()), events);
        Assert.IsTrue(presenter.Open(Player));
        Assert.IsTrue(presenter.Open(other));
        Assert.IsTrue(hud.Definition(PanoramaMenuPresenter.HudId)!.CaptureInput);
        Assert.AreEqual("<b>Row 0</b>", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_0_text"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_next");
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_next");
        Assert.AreEqual("2 / 2", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_page"));
        Assert.AreEqual("1 / 2", hud.Text(other, PanoramaMenuPresenter.HudId, "ano_menu_page"));
        Assert.IsTrue(hud.Class(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_2", "hidden"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_5");
        Assert.AreEqual(-1, selected);
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_0");
        Assert.AreEqual(6, selected);
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_close");
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
        CollectionAssert.AreEquivalent(new[] { other }, hud.VisiblePlayers(PanoramaMenuPresenter.HudId).ToArray());
    }

    [TestMethod]
    public async Task ClosedOrReconnectedSessionCannotBeReopenedByPendingClick()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var menus = new MenuService();
        var hud = new TestCustomHudService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = menus.Register(new("test"), new(new("ano.test.menu"), "Test",
            [new("wait", "Wait", async _ => { entered.SetResult(); await release.Task; }, keepOpen: true)]));
        menus.Open(Player, new("ano.test.menu"));
        using var presenter = new PanoramaMenuPresenter(hud, menus, players, new CommandRegistry(new Permissions()), events);
        presenter.Open(Player);
        var pending = hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_0").AsTask();
        await entered.Task;
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_close");
        await players.ConnectAsync(new(Player, "Replacement", PlayerTeam.Terrorist, true, Now.AddSeconds(1)));
        release.SetResult();
        await pending;
        Assert.IsEmpty(hud.VisiblePlayers(PanoramaMenuPresenter.HudId));
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
    }

    [TestMethod]
    public async Task ReplacedLogicalMenuRejectsOldClickAndReconcileRefreshesIt()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var menus = new MenuService();
        var hud = new TestCustomHudService();
        var selections = 0;
        var registration = menus.Register(new("test"), new(new("ano.test.menu"), "Test",
            [new("item", "Old", _ => { selections++; return ValueTask.CompletedTask; })]));
        menus.Open(Player, new("ano.test.menu"));
        using var presenter = new PanoramaMenuPresenter(hud, menus, players, new CommandRegistry(new Permissions()), events);
        presenter.Open(Player);
        registration.Dispose();
        using var replacement = menus.Register(new("test"), new(new("ano.test.menu"), "New",
            [new("item", "New", _ => { selections++; return ValueTask.CompletedTask; })]));
        menus.Open(Player, new("ano.test.menu"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_0");
        Assert.AreEqual(0, selections);
        presenter.Reconcile();
        Assert.AreEqual("New", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
        presenter.CloseAll();
        Assert.IsEmpty(hud.VisiblePlayers(PanoramaMenuPresenter.HudId));
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
    }

    [TestMethod]
    public async Task HomeFiltersEnabledDestinationsAndRechecksRevokedPermissions()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var permissions = new Permissions();
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var executions = 0;
        using var command = commands.Register(new("test"), new("anorating", "Rating", new("ano.rating")), _ =>
        { executions++; return ValueTask.FromResult(CommandResult.Ok("Rating 42")); });
        using var home = new AnoHomeMenuModule(commands, players, menus, permissions, events);
        Assert.IsFalse((await commands.ExecuteAsync("!anomenu", null)).Success);
        await commands.ExecuteAsync("!anomenu", Player);
        menus.TryGetOpenMenu(Player, out var root);
        Assert.IsFalse(root!.Options.Any(option => option.Id == "anorating"));
        permissions.Allow = true;
        await commands.ExecuteAsync("!anomenu", Player);
        menus.TryGetOpenMenu(Player, out root);
        Assert.IsTrue(root!.Options.Any(option => option.Id == "anorating"));
        permissions.Allow = false;
        await menus.SelectAsync(Player, root, "anorating");
        Assert.AreEqual(0, executions);
        menus.TryGetOpenMenu(Player, out var detail);
        Assert.IsTrue(detail!.Options.Any(option => option.Id == "home"));
        await menus.SelectAsync(Player, "home");
        menus.TryGetOpenMenu(Player, out root);
        Assert.IsFalse(root!.Options.Any(option => option.Id == "administration"));
        home.Dispose();
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
        Assert.IsFalse(commands.GetCommands().Any(command => command.Name == "anomenu"));
    }

    [TestMethod]
    public async Task HomeReadFinishingAfterReconnectCannotReplaceNewSessionMenu()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new Permissions());
        var menus = new MenuService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var command = commands.Register(new("test"), new("anorating", "Rating"), async _ =>
        { entered.SetResult(); await release.Task; return CommandResult.Ok("Old result"); });
        using var home = new AnoHomeMenuModule(commands, players, menus, new Permissions(), events);
        await commands.ExecuteAsync("!anomenu", Player);
        var pending = menus.SelectAsync(Player, "anorating").AsTask();
        await entered.Task;
        await players.ConnectAsync(new(Player, "New", PlayerTeam.Terrorist, true, Now.AddSeconds(1)));
        await commands.ExecuteAsync("!anomenu", Player);
        release.SetResult();
        await pending;
        menus.TryGetOpenMenu(Player, out var root);
        Assert.AreEqual("AnoCore", root!.Title);
    }

    [TestMethod]
    public async Task DelayedHomeReadCannotOverwriteAnotherFeatureMenu()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new Permissions());
        var menus = new MenuService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var command = commands.Register(new("test"), new("anorating", "Rating"), async _ =>
        { entered.SetResult(); await release.Task; return CommandResult.Ok("Old result"); });
        using var home = new AnoHomeMenuModule(commands, players, menus, new Permissions(), events);
        await commands.ExecuteAsync("!anomenu", Player);
        var pending = menus.SelectAsync(Player, "anorating").AsTask();
        await entered.Task;
        var other = new MenuDefinition(new("ano.test.other"), "New feature", [new("info", "New data", _ => ValueTask.CompletedTask)]);
        using var registration = menus.Register(new("test"), other);
        menus.Open(Player, other.Id);
        release.SetResult();
        await pending;
        menus.TryGetOpenMenu(Player, out var current);
        Assert.AreSame(other, current);
    }

    [TestMethod]
    public void RegistrationFailureReleasesHudAndHomeCommand()
    {
        var hud = new TestCustomHudService();
        var commands = new CommandRegistry(new Permissions());
        var players = new PlayerRegistry(new AnoEventBus());
        var menus = new MenuService();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new PanoramaMenuPresenter(hud, menus, players, commands, new RejectEvents()));
        Assert.IsNull(hud.Definition(PanoramaMenuPresenter.HudId));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new AnoHomeMenuModule(commands, players, menus, new Permissions(), new RejectEvents()));
        Assert.IsEmpty(commands.GetCommands());
    }

    [TestMethod]
    public async Task FeatureMenusAndHomeUseTheSameHudAndDisconnectClosesLogicalState()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var player = await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new Permissions());
        var menus = new MenuService();
        var hud = new TestCustomHudService();
        var feature = new MenuDefinition(new("ano.test.stats"), "Statistics", [new("kills", "Kills: 12", _ => ValueTask.CompletedTask, keepOpen: true)]);
        using var registration = menus.Register(new("test"), feature);
        using var command = commands.Register(new("test"), new("anostatsmenu", "Stats"), _ =>
        { menus.Open(Player, feature.Id); return ValueTask.FromResult(CommandResult.Ok()); });
        using var home = new AnoHomeMenuModule(commands, players, menus, new Permissions(), events);
        using var presenter = new PanoramaMenuPresenter(hud, menus, players, commands, events);
        await commands.ExecuteAsync("!anomenu", Player);
        presenter.Open(Player);
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_0");
        Assert.AreEqual("Statistics", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_home");
        Assert.AreEqual("AnoCore", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
        menus.Open(Player, feature.Id);
        presenter.Open(Player);
        await players.DisconnectAsync(Player, player.SessionId, Now.AddSeconds(1));
        Assert.IsEmpty(hud.VisiblePlayers(PanoramaMenuPresenter.HudId));
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
    }

    private sealed class RejectEvents : IAnoEventBus
    {
        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler) where TEvent : IAnoEvent
            => throw new InvalidOperationException("registration");
        public ValueTask PublishAsync<TEvent>(TEvent value, CancellationToken cancellationToken = default) where TEvent : IAnoEvent
            => throw new AssertFailedException();
    }

    private sealed class Permissions : IPermissionEvaluator
    {
        public bool Allow { get; set; }
        public ValueTask<bool> HasPermissionAsync(PlayerId playerId, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Allow);
    }
}
