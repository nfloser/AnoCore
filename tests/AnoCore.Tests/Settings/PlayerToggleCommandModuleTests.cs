using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
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

        public ValueTask<T> GetAsync<T>(PlayerId id, PlayerSettingKey<T> key,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_values.TryGetValue(key.Name, out var value)
                ? (T)(object)value : key.DefaultValue);

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
