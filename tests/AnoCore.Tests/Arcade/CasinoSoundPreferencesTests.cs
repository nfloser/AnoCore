using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Modules.Arcade.Casino;
using AnoCore.Runtime.Commands;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Runtime.Settings;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class CasinoSoundPreferencesTests
{
    private static readonly PlayerId One = new(76561198000050801);
    private static readonly PlayerId Two = new(76561198000050802);

    [TestMethod]
    public async Task DefaultOn_ToggleOffAndOn_AffectsOnlyCallerAndSurvivesReconnect()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await ConnectAsync(players, One, "One");
        await ConnectAsync(players, Two, "Two");
        var settings = new FakeSettings(events);
        var catalog = new PlayerToggleCatalog();
        var commands = new CommandRegistry(new AllowAll());
        using var preferences = new CasinoSoundPreferences(commands, catalog, settings, players, events);

        // Do not play before reading the saved option, even when default-on.
        Assert.IsFalse(preferences.IsEnabled(One));
        await preferences.RefreshAsync(One);
        await preferences.RefreshAsync(Two);
        Assert.IsTrue(preferences.IsEnabled(One));
        Assert.IsTrue(preferences.IsEnabled(Two));

        var off = await commands.ExecuteAsync("!anocasinosound", One);
        Assert.IsTrue(off.Success);
        StringAssert.Contains(off.Message, "AUS");
        Assert.IsFalse(preferences.IsEnabled(One));
        Assert.IsTrue(preferences.IsEnabled(Two));

        var old = players.OnlinePlayers.Single(x => x.Id == One);
        await players.DisconnectAsync(One, old.SessionId, DateTimeOffset.UtcNow);
        Assert.IsFalse(preferences.IsEnabled(One));
        await ConnectAsync(players, One, "One reconnect");
        Assert.IsFalse(preferences.IsEnabled(One)); // stale client cannot trigger sound.
        await preferences.RefreshAsync(One);
        Assert.IsFalse(preferences.IsEnabled(One)); // stored off survives the reconnect.

        var on = await commands.ExecuteAsync("!anocasinosound", One);
        Assert.IsTrue(on.Success);
        StringAssert.Contains(on.Message, "AN");
        Assert.IsTrue(preferences.IsEnabled(One));
        Assert.IsTrue(await settings.GetAsync(One, CasinoSoundPreferences.Key));
    }

    [TestMethod]
    public async Task SharedSettingsMenu_UpdatesLiveSoundPreference_WithoutDuplicateStore()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await ConnectAsync(players, One, "One");
        var settings = new FakeSettings(events);
        var catalog = new PlayerToggleCatalog();
        var commands = new CommandRegistry(new AllowAll());
        using var preferences = new CasinoSoundPreferences(commands, catalog, settings, players, events);
        using var toggles = new PlayerToggleCommandModule(commands, players, catalog, settings);
        Assert.IsTrue(catalog.TryGet(CasinoSoundPreferences.Key.Name, out var option));
        Assert.AreEqual("Casino-Walzensound", option!.Label);

        await preferences.RefreshAsync(One);
        Assert.IsTrue(preferences.IsEnabled(One));
        Assert.IsTrue((await commands.ExecuteAsync("!anotoggle casino.reel_sound off", One)).Success);
        Assert.IsFalse(preferences.IsEnabled(One));
        Assert.IsTrue((await commands.ExecuteAsync("!anotoggle casino.reel_sound default", One)).Success);
        Assert.IsTrue(preferences.IsEnabled(One));

        preferences.Dispose();
        Assert.IsFalse(catalog.TryGet(CasinoSoundPreferences.Key.Name, out _));
        Assert.AreEqual(CommandFailureReason.NotFound,
            (await commands.ExecuteAsync("!anocasinosound", One)).FailureReason);
    }

    [TestMethod]
    public async Task ConsoleDisconnectedOrExtraArguments_CannotChangePreference()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        await ConnectAsync(players, One, "One");
        var settings = new FakeSettings(events);
        var commands = new CommandRegistry(new AllowAll());
        using var preferences = new CasinoSoundPreferences(
            commands, new PlayerToggleCatalog(), settings, players, events);

        Assert.IsFalse((await commands.ExecuteAsync("!anocasinosound", null)).Success);
        Assert.IsFalse((await commands.ExecuteAsync("!anocasinosound 50", One)).Success);
        Assert.AreEqual(0, settings.WriteCount);
        var old = players.OnlinePlayers.Single();
        await players.DisconnectAsync(One, old.SessionId, DateTimeOffset.UtcNow);
        Assert.IsFalse((await commands.ExecuteAsync("!anocasinosound", One)).Success);
        Assert.AreEqual(0, settings.WriteCount);
    }

    private static Task ConnectAsync(PlayerRegistry players, PlayerId player, string name)
        => players.ConnectAsync(new PlayerConnection(
            player, name, PlayerTeam.Terrorist, true, DateTimeOffset.UtcNow)).AsTask();

    private sealed class AllowAll : IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId player, PermissionId permission,
            CancellationToken token = default) => ValueTask.FromResult(true);
    }

    private sealed class FakeSettings(IAnoEventBus events) : IPlayerSettingsService
    {
        private readonly Dictionary<(PlayerId, string), bool> _values = [];
        public int WriteCount { get; private set; }

        public ValueTask<T> GetAsync<T>(PlayerId player, PlayerSettingKey<T> key,
            CancellationToken token = default)
            => ValueTask.FromResult(_values.TryGetValue((player, key.Name), out var value)
                ? (T)(object)value : key.DefaultValue);

        public async ValueTask SetAsync<T>(PlayerId player, PlayerSettingKey<T> key, T value,
            CancellationToken token = default)
        {
            _values[(player, key.Name)] = (bool)(object)value!;
            WriteCount++;
            await events.PublishAsync(new PlayerSettingChangedEvent(
                player, key.Name, PlayerSettingChangeKind.Set), token);
        }

        public async ValueTask<bool> ResetAsync<T>(PlayerId player, PlayerSettingKey<T> key,
            CancellationToken token = default)
        {
            if (!_values.Remove((player, key.Name))) return false;
            await events.PublishAsync(new PlayerSettingChangedEvent(
                player, key.Name, PlayerSettingChangeKind.Reset), token);
            return true;
        }
    }
}
