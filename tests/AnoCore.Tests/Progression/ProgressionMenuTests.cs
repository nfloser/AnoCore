using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Modules.Progression;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Menus;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class ProgressionMenuTests
{
    private static readonly PlayerId Player = new(76561198000295101);
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 13, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task RootOnlyShowsEnabledReadSourcesAndLifetimeFallback()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await Connect(players);
        var commands = new CommandRegistry(new Allow());
        var menus = new MenuService();
        using var level = Register(commands, "anolevel", _ => ValueTask.FromResult(CommandResult.Ok("Level 2")));
        using var module = new ProgressionMenuModule(commands, players, menus, events);
        Assert.IsTrue((await commands.ExecuteAsync("!anoprogression", Player)).Success);
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out var root));
        Assert.HasCount(1, root!.Options);
        Assert.AreEqual("anolevel", root.Options[0].Id);
        using var xp = Register(commands, "anoxp", _ => ValueTask.FromResult(CommandResult.Ok("XP 100")));
        await commands.ExecuteAsync("!anoprogression", Player);
        menus.TryGetOpenMenu(Player, out root);
        Assert.HasCount(1, root!.Options);
        Assert.AreEqual("anoxp", root.Options[0].Id);
        Assert.AreEqual(CommandFailureReason.Forbidden, (await commands.ExecuteAsync("!anoprogression", null)).FailureReason);
    }

    [TestMethod]
    public async Task DetailsReuseCommandsWithBoundedEscapedTextAndBackNavigation()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await Connect(players);
        var commands = new CommandRegistry(new Allow());
        var menus = new MenuService();
        var reads = 0;
        using var xp = Register(commands, "anoxp", context =>
        {
            Assert.AreEqual(Player, context.Caller);
            reads++;
            return ValueTask.FromResult(CommandResult.Ok("<b>XP</b>\n" + new string('x', 500) + " | Level 2"));
        });
        using var module = new ProgressionMenuModule(commands, players, menus, events);
        await commands.ExecuteAsync("!anoprogression", Player);
        await menus.SelectAsync(Player, "anoxp");
        menus.TryGetOpenMenu(Player, out var detail);
        Assert.HasCount(4, detail!.Options);
        StringAssert.Contains(detail.Options[0].Label, "&lt;b&gt;");
        Assert.IsFalse(detail.Options[0].Label.Contains('<'));
        Assert.IsFalse(detail.Options[0].Label.Contains('\n'));
        Assert.IsTrue(detail.Options[0].Label.Length < 200);
        await menus.SelectAsync(Player, "refresh");
        Assert.AreEqual(2, reads);
        await menus.SelectAsync(Player, "back");
        menus.TryGetOpenMenu(Player, out var root);
        Assert.AreEqual("Progression", root!.Title);
    }

    [TestMethod]
    public async Task PagedViewsPassPageAndStopNavigationAfterCommandFailure()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await Connect(players);
        var commands = new CommandRegistry(new Allow());
        var menus = new MenuService();
        using var challenges = Register(commands, "anochallenges", context =>
        {
            var page = context.Get<int>("page");
            return ValueTask.FromResult(page <= 2 ? CommandResult.Ok("Challenge page " + page)
                : CommandResult.Fail(CommandFailureReason.InvalidInput, "Choose page 1-2."));
        }, paged: true);
        using var module = new ProgressionMenuModule(commands, players, menus, events);
        await commands.ExecuteAsync("!anoprogression", Player);
        await menus.SelectAsync(Player, "anochallenges");
        await menus.SelectAsync(Player, "next");
        menus.TryGetOpenMenu(Player, out var second);
        StringAssert.Contains(second!.Title, "page 2");
        await menus.SelectAsync(Player, "next");
        menus.TryGetOpenMenu(Player, out var invalid);
        Assert.IsFalse(invalid!.Options.Any(item => item.Id == "next"));
        Assert.IsTrue(invalid.Options.Any(item => item.Id == "previous"));
        await menus.SelectAsync(Player, "previous");
        menus.TryGetOpenMenu(Player, out var restored);
        StringAssert.Contains(restored!.Options[0].Label, "page 2");
    }

    [TestMethod]
    public async Task NewestReadWinsWhenViewsFinishOutOfOrder()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await Connect(players);
        var commands = new CommandRegistry(new Allow());
        var menus = new MenuService();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var slow = Register(commands, "anoxp", async _ => { started.SetResult(); await release.Task; return CommandResult.Ok("OLD"); });
        using var fast = Register(commands, "anoachievements", _ => ValueTask.FromResult(CommandResult.Ok("NEW")), true);
        using var module = new ProgressionMenuModule(commands, players, menus, events);
        await commands.ExecuteAsync("!anoprogression", Player);
        menus.TryGetOpenMenu(Player, out var root);
        var pending = root!.Options.Single(item => item.Id == "anoxp").OnSelected(new(Player, root.Id, "anoxp", default)).AsTask();
        await started.Task;
        await root.Options.Single(item => item.Id == "anoachievements").OnSelected(new(Player, root.Id, "anoachievements", default));
        release.SetResult();
        await pending;
        menus.TryGetOpenMenu(Player, out var detail);
        Assert.AreEqual("NEW", detail!.Options[0].Label);
    }

    [TestMethod]
    public async Task ReconnectSuppressesInFlightReadAndStaleRootCallbacks()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await Connect(players);
        var commands = new CommandRegistry(new Allow());
        var menus = new MenuService();
        using var xp = Register(commands, "anoxp", async _ => { await Connect(players); return CommandResult.Ok("STALE"); });
        using var module = new ProgressionMenuModule(commands, players, menus, events);
        await commands.ExecuteAsync("!anoprogression", Player);
        menus.TryGetOpenMenu(Player, out var old);
        await menus.SelectAsync(Player, "anoxp");
        await commands.ExecuteAsync("!anoprogression", Player);
        menus.TryGetOpenMenu(Player, out var current);
        await old!.Options[0].OnSelected(new(Player, old.Id, old.Options[0].Id, default));
        menus.TryGetOpenMenu(Player, out var after);
        Assert.AreSame(current, after);
        Assert.AreEqual("Progression", after!.Title);
    }

    [TestMethod]
    public async Task DisconnectAndDisposeRemoveOwnedMenusAndCommands()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var player = await Connect(players);
        var commands = new CommandRegistry(new Allow());
        var menus = new MenuService();
        using var module = new ProgressionMenuModule(commands, players, menus, events);
        await commands.ExecuteAsync("!anoprogression", Player);
        await players.DisconnectAsync(Player, player.SessionId, Now);
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
        await Connect(players);
        await commands.ExecuteAsync("!anoprogression", Player);
        module.Dispose();
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
        Assert.AreEqual(CommandFailureReason.NotFound, (await commands.ExecuteAsync("!anoprogression", Player)).FailureReason);
    }

    [TestMethod]
    public async Task UnloadDuringReadCannotReopenMenu()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await Connect(players);
        var commands = new CommandRegistry(new Allow());
        var menus = new MenuService();
        ProgressionMenuModule? module = null;
        using var xp = Register(commands, "anoxp", _ =>
        {
            module!.Dispose();
            return ValueTask.FromResult(CommandResult.Ok("STALE"));
        });
        using (module = new ProgressionMenuModule(commands, players, menus, events))
        {
            await commands.ExecuteAsync("!anoprogression", Player);
            await menus.SelectAsync(Player, "anoxp");
            Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
        }
    }

    [TestMethod]
    public void FailedSubscriptionRollsBackMenuCommand()
    {
        var commands = new CommandRegistry(new Allow());
        Assert.ThrowsExactly<InvalidOperationException>(() => new ProgressionMenuModule(commands,
            new PlayerRegistry(new AnoEventBus()), new MenuService(), new RejectEvents()));
        Assert.IsEmpty(commands.GetCommands());
    }

    private static Task<PlayerSnapshot> Connect(PlayerRegistry players)
        => players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now)).AsTask();

    private static IDisposable Register(IAnoCommandRegistry commands, string name, AnoCommandHandler handler, bool paged = false)
        => commands.Register(new ModuleId("test"), new(name, "Test", arguments: paged
            ? [new("page", CommandArgumentKind.Int32, "Page")] : []), handler);

    private sealed class Allow : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class RejectEvents : IAnoEventBus
    {
        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler) where TEvent : IAnoEvent
            => throw new InvalidOperationException("registration");
        public ValueTask PublishAsync<TEvent>(TEvent value, CancellationToken cancellationToken = default) where TEvent : IAnoEvent
            => throw new AssertFailedException();
    }
}
