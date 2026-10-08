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
    public async Task SuppressedPageIndicatorKeepsPlainStatsTitleAndNativeNavigation()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var menus = new MenuService();
        var hud = new TestCustomHudService();
        var next = false;
        var definition = new MenuDefinition(new("ano.test.stats"), "Stats",
            [new("value", "#1 Name: 100", _ => ValueTask.CompletedTask, keepOpen: true),
             new("next", "Next page", _ => { next = true; return ValueTask.CompletedTask; }, keepOpen: true)])
        { SuppressPageIndicator = true };
        using var registered = menus.Register(new("test"), definition);
        menus.Open(Player, definition.Id);
        using var presenter = new PanoramaMenuPresenter(hud, menus, players, new CommandRegistry(new Permissions()), events);
        Assert.IsTrue(presenter.Open(Player));
        Assert.AreEqual("Stats", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
        Assert.AreEqual(string.Empty, hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_page"));
        Assert.IsTrue(hud.Class(Player, PanoramaMenuPresenter.HudId, "ano_menu_previous", "disabled"));
        Assert.IsFalse(hud.Class(Player, PanoramaMenuPresenter.HudId, "ano_menu_next", "disabled"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_next");
        Assert.IsTrue(next);
        Assert.IsFalse(new MenuDefinition(new("ano.test.default"), "Default", []).SuppressPageIndicator);
    }

    [TestMethod]
    public async Task SourcePaginationUsesFooterWithoutDuplicateRows()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var menus = new MenuService();
        var hud = new TestCustomHudService();
        var next = false;
        using var registration = menus.Register(new("test"), new(new("ano.test.paging"), "Settings — page 1/2",
        [new("item", "Setting", _ => ValueTask.CompletedTask, keepOpen: true),
         new("next", "Next page", _ => { next = true; return ValueTask.CompletedTask; }, keepOpen: true)]));
        menus.Open(Player, new("ano.test.paging"));
        using var presenter = new PanoramaMenuPresenter(hud, menus, players, new CommandRegistry(new Permissions()), events);
        Assert.IsTrue(presenter.Open(Player));
        Assert.AreEqual("Settings", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
        Assert.AreEqual("1 / 2", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_page"));
        Assert.IsTrue(hud.Class(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_1", "hidden"));
        Assert.IsFalse(hud.Class(Player, PanoramaMenuPresenter.HudId, "ano_menu_next", "disabled"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_next");
        Assert.IsTrue(next);
    }

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
        Assert.IsFalse((await commands.ExecuteAsync("!anomenunavigation", null)).Success);
        await commands.ExecuteAsync("!anomenunavigation", Player);
        menus.TryGetOpenMenu(Player, out var root);
        Assert.IsFalse(root!.Options.Any(option => option.Id == "anorating"));
        permissions.Allow = true;
        await commands.ExecuteAsync("!anomenunavigation", Player);
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
        await commands.ExecuteAsync("!anomenunavigation", Player);
        var pending = menus.SelectAsync(Player, "anorating").AsTask();
        await entered.Task;
        await players.ConnectAsync(new(Player, "New", PlayerTeam.Terrorist, true, Now.AddSeconds(1)));
        await commands.ExecuteAsync("!anomenunavigation", Player);
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
        await commands.ExecuteAsync("!anomenunavigation", Player);
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
        await commands.ExecuteAsync("!anomenunavigation", Player);
        presenter.Open(Player);
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_0");
        Assert.AreEqual("Statistics", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_home");
        Assert.AreEqual("Your dashboard", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
        menus.Open(Player, feature.Id);
        presenter.Open(Player);
        await players.DisconnectAsync(Player, player.SessionId, Now.AddSeconds(1));
        Assert.IsEmpty(hud.VisiblePlayers(PanoramaMenuPresenter.HudId));
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
    }

    [TestMethod]
    public async Task DashboardUsesPersonalReadResultsAndNativeButtonsSelectActions()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Nille", PlayerTeam.Terrorist, true, Now));
        var other = new PlayerId(76561198000295102);
        await players.ConnectAsync(new(other, "Other", PlayerTeam.CounterTerrorist, true, Now));
        var permissions = new Permissions();
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var hud = new TestCustomHudService();
        var xp = 12;
        using var rank = commands.Register(new("test"), new("anorank", "Rank"), context =>
            ValueTask.FromResult(CommandResult.Ok("Rank for " + context.Caller!.SteamId64)));
        using var progression = commands.Register(new("test"), new("anoxp", "XP"), _ =>
            ValueTask.FromResult(CommandResult.Ok("XP " + xp)));
        using var challenges = commands.Register(new("test"), new("anochallenges", "Challenges"), _ =>
            ValueTask.FromResult(CommandResult.Ok("Challenges 1/1 | First: 2/10 | Second: 3/20 | Third: 4/30")));
        using var combat = commands.Register(new("test"), new("anokda", "KDA"), _ =>
            ValueTask.FromResult(CommandResult.Ok("[ANO] 20 kill(s), 10 death(s), 3 assist(s).")));
        using var gameplay = commands.Register(new("test"), new("anogamestats", "Gameplay"), _ =>
            ValueTask.FromResult(CommandResult.Ok("[ANO] Gameplay stats: HeadshotKill=5, MatchWon=3, MatchLost=1, RoundWon=99")));
        using var playtime = commands.Register(new("test"), new("anoplaytime", "Playtime"), _ =>
            ValueTask.FromResult(CommandResult.Ok("[ANO] Playtime: 1.02:03:04.5678900; today (UTC): 00:01:46.0269000. Breakdown: UNKNOWN/alive 00:01:52")));
        using var home = new AnoHomeMenuModule(commands, players, menus, permissions, events);
        using var presenter = new PanoramaMenuPresenter(hud, menus, players, commands, events);
        Assert.IsFalse((await commands.ExecuteAsync("!anomenu", null)).Success);
        await commands.ExecuteAsync("!anomenu", Player);
        await commands.ExecuteAsync("!anomenu", other);
        menus.TryGetOpenMenu(Player, out var first);
        menus.TryGetOpenMenu(other, out var second);
        Assert.IsNotNull(first!.Dashboard);
        StringAssert.Contains(first.Dashboard.Profile, "Nille");
        StringAssert.Contains(second!.Dashboard!.Profile, "Other");
        Assert.AreEqual("First: 2/10", first.Dashboard.Challenge1);
        Assert.AreEqual("Second: 3/20", first.Dashboard.Challenge2);
        StringAssert.Contains(first.Dashboard.Statistics, "HS: 25%");
        StringAssert.Contains(first.Dashboard.Statistics, "Match wins: 75%");
        Assert.AreEqual("Playtime: 1d 2h 3m · Today: 0d 0h 1m", System.Net.WebUtility.HtmlDecode(first.Dashboard.Playtime));
        presenter.Open(Player);
        Assert.IsTrue(hud.Class(Player, PanoramaMenuPresenter.HudId, "ano_menu_root", "dashboard"));
        Assert.AreEqual("All challenges", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_0_text"));
        Assert.AreEqual("1 / 1", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_page"));
        xp = 99;
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_2");
        Assert.AreEqual("XP 99", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_dashboard_progression"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_1");
        Assert.AreEqual("AnoCore", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
        Assert.IsFalse(hud.Class(Player, PanoramaMenuPresenter.HudId, "ano_menu_root", "dashboard"));
        Assert.AreEqual(string.Empty, hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_dashboard_profile"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_home");
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_0");
        Assert.AreEqual("Challenges", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
        Assert.IsTrue(hud.Class(Player, PanoramaMenuPresenter.HudId, "ano_menu_next", "disabled"));
        Assert.AreEqual("XP 12", second.Dashboard.Progression);
    }

    [TestMethod]
    public async Task DashboardDoesNotReadForbiddenCards()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var permissions = new Permissions();
        var commands = new CommandRegistry(permissions);
        var menus = new MenuService();
        var reads = 0;
        using var command = commands.Register(new("test"), new("anochallenges", "Private", permission: new("ano.private.read")), _ =>
        { reads++; return ValueTask.FromResult(CommandResult.Ok("Private data")); });
        using var home = new AnoHomeMenuModule(commands, players, menus, permissions, events);
        await commands.ExecuteAsync("!anomenu", Player);
        menus.TryGetOpenMenu(Player, out var menu);
        Assert.AreEqual(0, reads);
        Assert.IsFalse(menu!.Options.Any(option => option.Id == "dashboard_challenges"));
        Assert.AreEqual("Unavailable", menu.Dashboard!.Challenge1);
    }

    [TestMethod]
    public async Task DelayedDashboardReadCannotOverwriteAFeatureMenu()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new Permissions());
        var menus = new MenuService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var command = commands.Register(new("test"), new("anorank", "Rank"), async _ =>
        { entered.SetResult(); await release.Task; return CommandResult.Ok("Rank"); });
        using var home = new AnoHomeMenuModule(commands, players, menus, new Permissions(), events);
        var pending = commands.ExecuteAsync("!anomenu", Player).AsTask();
        await entered.Task;
        var feature = new MenuDefinition(new("ano.feature"), "Feature", []);
        using var registration = menus.Register(new("test"), feature);
        menus.Open(Player, feature.Id);
        release.SetResult();
        await pending;
        menus.TryGetOpenMenu(Player, out var current);
        Assert.AreSame(feature, current);
    }

    [TestMethod]
    public async Task DashboardFromPreviousSessionCannotReplaceReconnectedNavigation()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Previous", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new Permissions());
        var menus = new MenuService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var command = commands.Register(new("test"), new("anorank", "Rank"), async _ =>
        { entered.SetResult(); await release.Task; return CommandResult.Ok("Previous rank"); });
        using var home = new AnoHomeMenuModule(commands, players, menus, new Permissions(), events);
        var pending = commands.ExecuteAsync("!anomenu", Player).AsTask();
        await entered.Task;
        await players.ConnectAsync(new(Player, "Replacement", PlayerTeam.CounterTerrorist, true, Now.AddSeconds(1)));
        await commands.ExecuteAsync("!anomenunavigation", Player);
        menus.TryGetOpenMenu(Player, out var navigation);
        release.SetResult();
        await pending;
        menus.TryGetOpenMenu(Player, out var current);
        Assert.AreSame(navigation, current);
        Assert.IsNull(current!.Dashboard);
    }

    [TestMethod]
    public void DashboardExtensionRetainsTheOriginalMenuConstructor()
    {
        var constructor = typeof(MenuDefinition).GetConstructor(
            [typeof(MenuId), typeof(string), typeof(IReadOnlyCollection<MenuOption>)]);
        Assert.IsNotNull(constructor);
        var menu = new MenuDefinition(new("ano.compatibility"), "Compatibility", []);
        Assert.IsNull(menu.Dashboard);
    }

    [TestMethod]
    public async Task BackWorksOnFirstPageAndReturnsDashboardFromNavigationAndChallenges()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new Permissions());
        var menus = new MenuService();
        var hud = new TestCustomHudService();
        using var challenges = commands.Register(new("test"), new("anochallenges", "Challenges"), _ =>
            ValueTask.FromResult(CommandResult.Ok("Challenges 1/1 | Weekly: 2/10")));
        using var home = new AnoHomeMenuModule(commands, players, menus, new Permissions(), events);
        using var presenter = new PanoramaMenuPresenter(hud, menus, players, commands, events);
        await commands.ExecuteAsync("!anomenu", Player);
        presenter.Open(Player);
        Assert.IsTrue(hud.Class(Player, PanoramaMenuPresenter.HudId, "ano_menu_back", "disabled"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_back");
        Assert.AreEqual("Your dashboard", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_1");
        Assert.IsFalse(hud.Class(Player, PanoramaMenuPresenter.HudId, "ano_menu_back", "disabled"));
        Assert.IsTrue(hud.Class(Player, PanoramaMenuPresenter.HudId, "ano_menu_previous", "disabled"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_back");
        Assert.AreEqual("Your dashboard", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_row_0");
        Assert.AreEqual("Challenges", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
        Assert.IsTrue(hud.Class(Player, PanoramaMenuPresenter.HudId, "ano_menu_next", "disabled"));
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_back");
        Assert.AreEqual("Your dashboard", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
    }

    [TestMethod]
    public async Task BackUsesTheFeatureParentActionWithoutChangingPageNavigation()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var menus = new MenuService();
        var hud = new TestCustomHudService();
        var selected = 0;
        var options = Enumerable.Range(0, 7).Select(index => new MenuOption("item" + index,
            "Item " + index, _ => ValueTask.CompletedTask, keepOpen: true)).ToList();
        options.Add(new("back", "Back to parent", _ => { selected++; return ValueTask.CompletedTask; }, keepOpen: true));
        using var registration = menus.Register(new("test"), new(new("ano.test.back"), "Child", options));
        menus.Open(Player, new("ano.test.back"));
        using var presenter = new PanoramaMenuPresenter(hud, menus, players, new CommandRegistry(new Permissions()), events);
        presenter.Open(Player);
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_next");
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_previous");
        Assert.AreEqual("1 / 2", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_page"));
        Assert.AreEqual(0, selected);
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_back");
        Assert.AreEqual(1, selected);
    }

    [TestMethod]
    public async Task FeatureRootWithoutAParentActionReturnsToNavigation()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now));
        var commands = new CommandRegistry(new Permissions());
        var menus = new MenuService();
        var hud = new TestCustomHudService();
        using var home = new AnoHomeMenuModule(commands, players, menus, new Permissions(), events);
        using var feature = menus.Register(new("test"), new(new("ano.test.feature.root"), "Feature", []));
        menus.Open(Player, new("ano.test.feature.root"));
        using var presenter = new PanoramaMenuPresenter(hud, menus, players, commands, events);
        presenter.Open(Player);
        await hud.ClickAsync(Player, PanoramaMenuPresenter.HudId, "ano_menu_back");
        Assert.AreEqual("AnoCore", hud.Text(Player, PanoramaMenuPresenter.HudId, "ano_menu_title"));
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
