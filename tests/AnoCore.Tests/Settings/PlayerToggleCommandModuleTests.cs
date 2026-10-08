using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Menus;
using AnoCore.Runtime.Players;
using AnoCore.Runtime.Settings;

namespace AnoCore.Tests.Settings;

[TestClass]
public sealed class PlayerToggleCommandModuleTests
{
    private static readonly PlayerId Player = new(76561198000013201);

    [TestMethod]
    public async Task Commands_ListBoundedPagesAndPersistOnOffDefault()
    {
        var players = await ConnectedAsync();
        var catalog = new PlayerToggleCatalog();
        for (var index = 1; index <= 4; index++)
            catalog.Register(new ModuleId("tests"), new PlayerToggleSetting(
                new PlayerSettingKey<bool>($"test.option{index}", index == 4),
                $"Option {index}", ""));
        var settings = new MemorySettings();
        var commands = new CommandRegistry(new AllowAll());
        using var module = new PlayerToggleCommandModule(
            commands, players, catalog, settings);

        var page1 = await commands.ExecuteAsync("!anosettings", Player);
        Assert.IsTrue(page1.Success);
        StringAssert.Contains(page1.Message, "test.option1=off");
        StringAssert.Contains(page1.Message, "test.option3=off");
        Assert.IsFalse(page1.Message!.Contains("test.option4", StringComparison.Ordinal));
        var page2 = await commands.ExecuteAsync("!anosettings 2", Player);
        StringAssert.Contains(page2.Message, "test.option4=on");

        Assert.IsTrue((await commands.ExecuteAsync(
            "!anotoggle test.option1 on", Player)).Success);
        Assert.IsTrue(await settings.GetAsync(Player,
            new PlayerSettingKey<bool>("test.option1", false)));
        Assert.IsTrue((await commands.ExecuteAsync(
            "!anotoggle test.option1 off", Player)).Success);
        Assert.IsFalse(await settings.GetAsync(Player,
            new PlayerSettingKey<bool>("test.option1", false)));
        Assert.IsTrue((await commands.ExecuteAsync(
            "!anotoggle test.option1 default", Player)).Success);
        Assert.AreEqual(1, settings.ResetCount);
    }

    [TestMethod]
    public async Task Commands_RejectConsoleUnknownKeysInvalidActionsAndDisconnectedPlayers()
    {
        var players = await ConnectedAsync();
        var catalog = new PlayerToggleCatalog();
        catalog.Register(new ModuleId("tests"), new PlayerToggleSetting(
            new PlayerSettingKey<bool>("test.option", false), "Option", ""));
        var settings = new MemorySettings();
        var commands = new CommandRegistry(new AllowAll());
        using var module = new PlayerToggleCommandModule(
            commands, players, catalog, settings);

        Assert.IsFalse((await commands.ExecuteAsync(
            "!anotoggle test.option on", null)).Success);
        Assert.IsFalse((await commands.ExecuteAsync(
            "!anotoggle unknown on", Player)).Success);
        Assert.IsFalse((await commands.ExecuteAsync(
            "!anotoggle test.option invalid", Player)).Success);
        Assert.IsFalse((await commands.ExecuteAsync(
            "!anosettings 0", Player)).Success);
        Assert.AreEqual(0, settings.SetCount);

        var old = players.OnlinePlayers.Single();
        await players.DisconnectAsync(Player, old.SessionId, DateTimeOffset.UtcNow);
        Assert.IsFalse((await commands.ExecuteAsync(
            "!anotoggle test.option on", Player)).Success);
        Assert.AreEqual(0, settings.SetCount);
        module.Dispose();
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anosettings", Player)).FailureReason);
    }

    [TestMethod]
    public async Task DelayedSettingsList_RejectsPreviousSessionAfterReconnect()
    {
        var players = await ConnectedAsync();
        var catalog = new PlayerToggleCatalog();
        catalog.Register(new ModuleId("tests"), new PlayerToggleSetting(
            new PlayerSettingKey<bool>("test.option", false), "Option", ""));
        var settings = new MemorySettings
        {
            ReadStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseRead = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var commands = new CommandRegistry(new AllowAll());
        using var module = new PlayerToggleCommandModule(
            commands, players, catalog, settings);
        var pending = commands.ExecuteAsync("!anosettings", Player).AsTask();
        await settings.ReadStarted.Task;
        await players.ConnectAsync(new PlayerConnection(
            Player, "New session", PlayerTeam.CounterTerrorist,
            true, DateTimeOffset.UtcNow));
        settings.ReleaseRead.SetResult();

        var result = await pending;
        Assert.IsFalse(result.Success);
        Assert.AreEqual(CommandFailureReason.InvalidInput, result.FailureReason);
    }

    [TestMethod]
    public async Task Menu_NavigatesAndTogglesRegisteredChoicesWithoutDuplicateDefaults()
    {
        var players = await ConnectedAsync();
        var catalog = new PlayerToggleCatalog();
        for (var index = 1; index <= 7; index++)
            catalog.Register(new ModuleId("tests"), new PlayerToggleSetting(
                new PlayerSettingKey<bool>($"test.option{index}", index == 4),
                $"Option {index}", ""));
        var settings = new MemorySettings();
        var commands = new CommandRegistry(new AllowAll());
        var menus = new MenuService();
        using var module = new PlayerToggleCommandModule(
            commands, players, catalog, settings, menus: menus);

        Assert.IsTrue((await commands.ExecuteAsync("!anosettingsmenu", Player)).Success);
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out var first));
        Assert.AreEqual(6, first!.Options.Count(option =>
            option.Label.StartsWith("Option", StringComparison.Ordinal)));
        var firstToggle = first.Options.Single(option =>
            option.Label.StartsWith("Option 1", StringComparison.Ordinal));
        Assert.IsTrue((await menus.SelectAsync(Player, firstToggle.Id)).Accepted);
        Assert.IsTrue(await settings.GetAsync(Player,
            new PlayerSettingKey<bool>("test.option1", false)));
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out var updated));
        Assert.IsFalse((await menus.SelectAsync(Player, firstToggle.Id)).Accepted);
        Assert.IsFalse(updated!.Options.Any(option =>
            option.Label.StartsWith("Default:", StringComparison.Ordinal)));
        var refreshedToggle = updated.Options.Single(option =>
            option.Label.StartsWith("Option 1", StringComparison.Ordinal));
        Assert.IsTrue((await menus.SelectAsync(Player, refreshedToggle.Id)).Accepted);
        Assert.IsFalse(await settings.GetAsync(Player,
            new PlayerSettingKey<bool>("test.option1", false)));
        Assert.AreEqual(0, settings.ResetCount);

        Assert.IsTrue(menus.TryGetOpenMenu(Player, out var afterToggle));
        var next = afterToggle!.Options.Single(option => option.Label == "Next page");
        await menus.SelectAsync(Player, next.Id);
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out var second));
        StringAssert.Contains(second!.Title, "page 2");
        Assert.IsTrue(second.Options.Any(option =>
            option.Label.StartsWith("Option 7", StringComparison.Ordinal)));
        Assert.IsFalse(second.Options.Any(option =>
            option.Label == "Next page"));
    }

    [TestMethod]
    public async Task Menu_RejectsOldSessionAndRemovesMatchingRegistrations()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await players.ConnectAsync(new PlayerConnection(
            Player, "First", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        var catalog = new PlayerToggleCatalog();
        catalog.Register(new ModuleId("tests"), new PlayerToggleSetting(
            new PlayerSettingKey<bool>("test.option", false), "Option", ""));
        var menus = new MenuService();
        var settings = new MemorySettings();
        var commands = new CommandRegistry(new AllowAll());
        using var module = new PlayerToggleCommandModule(
            commands, players, catalog, settings, menus, events);
        await commands.ExecuteAsync("!anosettingsmenu", Player);
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out var oldMenu));
        var staleOption = oldMenu!.Options.Single(option =>
            option.Label.StartsWith("Option", StringComparison.Ordinal));

        var old = players.OnlinePlayers.Single();
        await players.ConnectAsync(new PlayerConnection(
            Player, "Second", PlayerTeam.CounterTerrorist,
            true, DateTimeOffset.UtcNow));
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
        await commands.ExecuteAsync("!anosettingsmenu", Player);
        await events.PublishAsync(new AnoCore.Abstractions.Players.Events.PlayerDisconnectedEvent(
            old));
        Assert.IsTrue(menus.TryGetOpenMenu(Player, out _));
        Assert.IsFalse((await menus.SelectAsync(Player, staleOption.Id)).Accepted);
        Assert.AreEqual(0, settings.SetCount);

        module.Dispose();
        Assert.IsFalse(menus.TryGetOpenMenu(Player, out _));
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anosettingsmenu", Player)).FailureReason);
    }

    [TestMethod]
    public async Task RegistrationCollision_RollsBackFirstCommand()
    {
        var players = await ConnectedAsync();
        var commands = new CommandRegistry(new AllowAll());
        using var occupied = commands.Register(new ModuleId("occupied"),
            new CommandDescriptor("anotoggle", "Occupied"),
            _ => ValueTask.FromResult(CommandResult.Ok()));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new PlayerToggleCommandModule(
                commands, players, new PlayerToggleCatalog(), new MemorySettings()));
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anosettings", Player)).FailureReason);
    }

    private static async Task<PlayerRegistry> ConnectedAsync()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow));
        return players;
    }

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(
            PlayerId playerId, PermissionId permission,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
    }

    private sealed class MemorySettings : IPlayerSettingsService
    {
        private readonly Dictionary<string, bool> _values = [];
        public int SetCount { get; private set; }
        public int ResetCount { get; private set; }
        public TaskCompletionSource? ReadStarted { get; set; }
        public TaskCompletionSource? ReleaseRead { get; set; }

        public async ValueTask<T> GetAsync<T>(PlayerId id, PlayerSettingKey<T> key,
            CancellationToken cancellationToken = default)
        {
            ReadStarted?.TrySetResult();
            if (ReleaseRead is not null)
                await ReleaseRead.Task.WaitAsync(cancellationToken);
            return _values.TryGetValue(key.Name, out var value)
                ? (T)(object)value : key.DefaultValue;
        }

        public ValueTask SetAsync<T>(PlayerId id, PlayerSettingKey<T> key, T value,
            CancellationToken cancellationToken = default)
        {
            _values[key.Name] = (bool)(object)value!;
            SetCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> ResetAsync<T>(PlayerId id, PlayerSettingKey<T> key,
            CancellationToken cancellationToken = default)
        {
            ResetCount++;
            return ValueTask.FromResult(_values.Remove(key.Name));
        }
    }
}
