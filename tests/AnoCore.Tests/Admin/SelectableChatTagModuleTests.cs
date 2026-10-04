using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Menus;
using AnoCore.Runtime.Placeholders;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class SelectableChatTagModuleTests
{
    private static readonly PlayerId Player = new(76561198000012631);
    private string _root = null!;

    [TestInitialize]
    public void Initialize() => _root = Path.Combine(Path.GetTempPath(),
        "anocore-chat-tags-tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task Selection_IsPermissionGatedPersistedAndFallsBackAfterClearOrRevocation()
    {
        var config = await ConfigAsync();
        var permissions = new Permissions { Allowed = true };
        var settings = new Settings();
        var players = await ConnectedAsync();
        var registry = new PlaceholderRegistry();
        using var rank = registry.RegisterPrioritized(
            new("ranks"), "chat.tag", 0,
            (_, _) => ValueTask.FromResult<string?>("[Rank]"));
        var commands = new CommandRegistry(permissions);
        var refreshes = 0;
        using var module = await SelectableChatTagModule.CreateAsync(
            config, commands, registry, players, settings, permissions, permissions,
            (_, _) =>
            {
                refreshes++;
                return ValueTask.CompletedTask;
            });
        var context = new PlaceholderContext(
            new Dictionary<string, object?> { ["player"] = Player });

        Assert.AreEqual("[Rank]", await registry.ResolveAsync("{chat.tag}", context));
        Assert.IsTrue((await commands.ExecuteAsync("!anosettag staff", Player)).Success);
        Assert.AreEqual("[Staff]", await registry.ResolveAsync("{chat.tag}", context));
        Assert.AreEqual(1, refreshes);

        permissions.Allowed = false;
        permissions.RaiseReload();
        Assert.AreEqual("[Rank]", await registry.ResolveAsync("{chat.tag}", context));
        Assert.AreEqual(2, refreshes);
        Assert.IsFalse((await commands.ExecuteAsync("!anosettag staff", Player)).Success);
        permissions.Allowed = true;
        Assert.IsTrue((await commands.ExecuteAsync("!anocleartag", Player)).Success);
        Assert.AreEqual("[Rank]", await registry.ResolveAsync("{chat.tag}", context));
        Assert.AreEqual(3, refreshes);

        module.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anosettag staff", Player)).FailureReason);
        permissions.RaiseReload();
        Assert.AreEqual(3, refreshes);
    }

    [TestMethod]
    public async Task UnavailableOptionsAreHiddenAndOldSessionCannotBeRefreshed()
    {
        var config = await ConfigAsync();
        var permissions = new Permissions();
        var settings = new Settings();
        var players = await ConnectedAsync();
        var registry = new PlaceholderRegistry();
        var commands = new CommandRegistry(permissions);
        var refreshes = 0;
        using var module = await SelectableChatTagModule.CreateAsync(
            config, commands, registry, players, settings, permissions, permissions,
            (_, _) =>
            {
                refreshes++;
                return ValueTask.CompletedTask;
            });

        Assert.AreEqual("No chat tags available.",
            (await commands.ExecuteAsync("!anotags", Player)).Message);
        Assert.AreEqual(CommandFailureReason.Forbidden,
            (await commands.ExecuteAsync("!anosettag staff", Player)).FailureReason);

        permissions.Allowed = true;
        var old = players.OnlinePlayers.Single();
        await players.DisconnectAsync(Player, old.SessionId, DateTimeOffset.UtcNow);
        permissions.RaiseReload();
        Assert.AreEqual(0, refreshes);
    }

    [TestMethod]
    public async Task AuthorizationReloadStartsRefreshForEveryPlayerBeforeSlowReadCompletes()
    {
        var config = await ConfigAsync();
        var permissions = new Permissions { Allowed = true };
        var settings = new Settings();
        var players = await ConnectedAsync();
        var other = new PlayerId(76561198000012632);
        await players.ConnectAsync(new PlayerConnection(
            other, "Other", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        var blocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new HashSet<PlayerId>();
        using var module = await SelectableChatTagModule.CreateAsync(
            config, new CommandRegistry(permissions),
            new PlaceholderRegistry(), players, settings, permissions, permissions,
            async (player, token) =>
            {
                started.Add(player.Id);
                if (player.Id == Player)
                    await blocker.Task.WaitAsync(token);
            });

        permissions.RaiseReload();

        CollectionAssert.AreEquivalent(
            new[] { Player, other }, started.ToArray());
        blocker.SetResult();
    }

    [TestMethod]
    public async Task MenuShowsFiveEligibleTagsAndNavigatesBoundedPages()
    {
        var config = new JsonConfigStore(_root);
        await config.SaveAsync("chat-tags", new SelectableChatTagConfiguration
        {
            Tags = Enumerable.Range(1, 7)
                .Select(index => new SelectableChatTag(
                    $"tag{index}", $"[Tag {index}]", "ano.chat.staff"))
                .ToList(),
        });
        var permissions = new Permissions { Allowed = true };
        var players = await ConnectedAsync();
        var menus = new MenuService();
        var commands = new CommandRegistry(permissions);
        using var module = await SelectableChatTagModule.CreateAsync(
            config, commands, new PlaceholderRegistry(), players,
            new Settings(), permissions, permissions,
            (_, _) => ValueTask.CompletedTask, menus: menus);

        Assert.IsTrue((await commands.ExecuteAsync("!anochatmenu", Player)).Success);
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out var first));
        Assert.AreEqual(5, first!.Options.Count(option =>
            option.Label.StartsWith("[Tag", StringComparison.Ordinal)));
        Assert.IsTrue(first.Options.Any(option => option.Id == "next"));
        Assert.IsFalse(first.Options.Any(option => option.Id == "previous"));

        await menus.SelectAsync(Player, "next");
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out var second));
        Assert.AreEqual(2, second!.Options.Count(option =>
            option.Label.StartsWith("[Tag", StringComparison.Ordinal)));
        Assert.IsTrue(second.Options.Any(option => option.Id == "previous"));
        Assert.IsFalse(second.Options.Any(option => option.Id == "next"));
    }

    [TestMethod]
    public async Task MenuSelectionRechecksPermissionAndSessionBeforeSaving()
    {
        var permissions = new Permissions { Allowed = true };
        var settings = new Settings();
        var players = await ConnectedAsync();
        var menus = new MenuService();
        var commands = new CommandRegistry(permissions);
        var refreshes = 0;
        using var module = await SelectableChatTagModule.CreateAsync(
            await ConfigAsync(), commands, new PlaceholderRegistry(), players,
            settings, permissions, permissions,
            (_, _) =>
            {
                refreshes++;
                return ValueTask.CompletedTask;
            }, menus: menus);

        await commands.ExecuteAsync("!anochatmenu", Player);
        permissions.Allowed = false;
        await menus.SelectAsync(Player, "tag0");
        Assert.AreEqual("", await settings.GetAsync(
            Player, new PlayerSettingKey<string>("chat.tag.selected", "")));
        Assert.AreEqual(0, refreshes);

        permissions.Allowed = true;
        await commands.ExecuteAsync("!anochatmenu", Player);
        var old = players.OnlinePlayers.Single();
        await players.ConnectAsync(new PlayerConnection(
            Player, "Reconnected", PlayerTeam.Terrorist,
            true, DateTimeOffset.UtcNow));
        await menus.SelectAsync(Player, "tag0");
        Assert.AreEqual("", await settings.GetAsync(
            Player, new PlayerSettingKey<string>("chat.tag.selected", "")));

        await commands.ExecuteAsync("!anochatmenu", Player);
        await menus.SelectAsync(Player, "tag0");
        Assert.AreEqual("staff", await settings.GetAsync(
            Player, new PlayerSettingKey<string>("chat.tag.selected", "")));
        Assert.AreEqual(1, refreshes);

        module.Dispose();
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anochatmenu", Player)).FailureReason);
    }

    [TestMethod]
    public async Task MenuRegistrationsFollowMatchingSessionEvents()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new PlayerConnection(
            Player, "First", PlayerTeam.Terrorist,
            true, DateTimeOffset.UtcNow));
        var permissions = new Permissions { Allowed = true };
        var menus = new MenuService();
        var commands = new CommandRegistry(permissions);
        using var module = await SelectableChatTagModule.CreateAsync(
            await ConfigAsync(), commands, new PlaceholderRegistry(), players,
            new Settings(), permissions, permissions,
            (_, _) => ValueTask.CompletedTask, menus: menus, events: events);
        var old = players.OnlinePlayers.Single();
        await commands.ExecuteAsync("!anochatmenu", Player);
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out _));

        await players.ConnectAsync(new PlayerConnection(
            Player, "Second", PlayerTeam.CounterTerrorist,
            true, DateTimeOffset.UtcNow));
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));

        await commands.ExecuteAsync("!anochatmenu", Player);
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out _));
        await events.PublishAsync(new AnoCore.Abstractions.Players.Events.PlayerDisconnectedEvent(
            old));
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out _));

        var current = players.OnlinePlayers.Single();
        await players.DisconnectAsync(Player, current.SessionId, DateTimeOffset.UtcNow);
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
    }

    [TestMethod]
    public async Task ConfigurationRejectsUnsafeTextAndDuplicateIdentifiers()
    {
        var config = new JsonConfigStore(_root);
        await config.SaveAsync("chat-tags", new SelectableChatTagConfiguration
        {
            Tags =
            [
                new("staff", "[Staff]", "ano.chat.staff"),
                new("staff", "{unsafe}", "other.permission"),
            ],
        });

        await Assert.ThrowsExactlyAsync<ConfigValidationException>(async () =>
            await SelectableChatTagModule.CreateAsync(
                config, new CommandRegistry(new Permissions()),
                new PlaceholderRegistry(), await ConnectedAsync(),
                new Settings(), new Permissions(), new Permissions(),
                (_, _) => ValueTask.CompletedTask));
    }

    [TestMethod]
    public async Task LateCommandCollisionRollsBackTagAndEarlierCommands()
    {
        var config = await ConfigAsync();
        var permissions = new Permissions();
        var commands = new CommandRegistry(permissions);
        using var occupied = commands.Register(
            new("occupied"), new CommandDescriptor("anocleartag", "occupied"),
            _ => ValueTask.FromResult(CommandResult.Ok()));
        var registry = new PlaceholderRegistry();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await SelectableChatTagModule.CreateAsync(
                config, commands, registry, await ConnectedAsync(),
                new Settings(), permissions, permissions,
                (_, _) => ValueTask.CompletedTask));

        Assert.IsFalse(registry.Contains("chat.tag"));
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anotags", Player)).FailureReason);
    }

    private async Task<JsonConfigStore> ConfigAsync()
    {
        var config = new JsonConfigStore(_root);
        await config.SaveAsync("chat-tags", new SelectableChatTagConfiguration
        {
            Tags = [new("staff", "[Staff]", "ano.chat.staff")],
        });
        return config;
    }

    private static async Task<PlayerRegistry> ConnectedAsync()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.CounterTerrorist,
            true, DateTimeOffset.UtcNow));
        return players;
    }

    private sealed class Permissions : IPermissionEvaluator, IAuthorizationReloadEvents
    {
        public bool Allowed { get; set; }
        public event Action? Reloaded;

        public void RaiseReload() => Reloaded?.Invoke();

        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId, PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Allowed);
    }

    private sealed class Settings : IPlayerSettingsService
    {
        private readonly Dictionary<PlayerId, string> _selected = [];

        public ValueTask<T> GetAsync<T>(
            PlayerId playerId, PlayerSettingKey<T> key,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_selected.TryGetValue(playerId, out var value)
                ? (T)(object)value
                : key.DefaultValue);

        public ValueTask SetAsync<T>(
            PlayerId playerId, PlayerSettingKey<T> key, T value,
            CancellationToken cancellationToken = default)
        {
            _selected[playerId] = (string)(object)value!;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> ResetAsync<T>(
            PlayerId playerId, PlayerSettingKey<T> key,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_selected.Remove(playerId));
    }
}
