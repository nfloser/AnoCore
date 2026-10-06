using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Modules.Progression;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Runtime.Settings;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class AchievementNotificationTests
{
    private static readonly PlayerId Player = new(76561198000256101);
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 16, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Notifications_UseCommittedAwardAndPinnedSessionWithoutReplayingEmptyResults()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var player = await Connect(players);
        var messages = new Messages();
        var toggles = new PlayerToggleCatalog();
        using var service = new AchievementNotificationService(players, new Settings(), toggles, messages);
        await service.NotifyAsync(player, "Headshots", [Unlock(1, 200)]);
        await service.NotifyAsync(player, "Headshots", []);
        Assert.HasCount(1, messages.Requests);
        StringAssert.Contains(messages.Requests[0].Text, "+200 XP");
        Assert.AreEqual(player.SessionId, messages.Requests[0].Target.SessionId);
        Assert.AreEqual(Player, messages.Requests[0].Target.PlayerId);
        Assert.IsTrue(toggles.TryGet(PreferenceName, out _));
        service.Dispose();
        Assert.IsFalse(toggles.TryGet(PreferenceName, out _));
        await service.NotifyAsync(player, "Headshots", [Unlock(2, 400)]);
        Assert.HasCount(1, messages.Requests);
    }

    [TestMethod]
    public async Task DisabledPreference_SuppressesChat()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var player = await Connect(players);
        var messages = new Messages();
        using var service = new AchievementNotificationService(players, new Settings { Enabled = false },
            new PlayerToggleCatalog(), messages);
        await service.NotifyAsync(player, "Headshots", [Unlock(1, 100)]);
        Assert.IsEmpty(messages.Requests);
    }

    [TestMethod]
    public async Task ReconnectDuringPreferenceRead_SuppressesOldSession()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var player = await Connect(players);
        var messages = new Messages();
        var settings = new Settings
        {
            BeforeRead = async () => { await Connect(players); },
        };
        using var service = new AchievementNotificationService(players, settings, new PlayerToggleCatalog(), messages);
        await service.NotifyAsync(player, "Headshots", [Unlock(1, 100)]);
        Assert.IsEmpty(messages.Requests);
    }

    [TestMethod]
    public async Task DeliveryFailure_DoesNotStopLaterTiersAndDiagnosticsCannotEscape()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var player = await Connect(players);
        var messages = new Messages { FailFirst = true };
        using var service = new AchievementNotificationService(players, new Settings(), new PlayerToggleCatalog(),
            messages, _ => throw new InvalidOperationException("diagnostic failure"));
        await service.NotifyAsync(player, "{Headshots}|", [Unlock(1, 100), Unlock(2, 200)]);
        Assert.HasCount(2, messages.Requests);
        StringAssert.Contains(messages.Requests[1].Text, "tier 2");
        Assert.IsFalse(messages.Requests[1].Text.Contains('{'));
    }

    private static string PreferenceName => AchievementNotificationService.Preference.Name;

    private static Task<PlayerSnapshot> Connect(PlayerRegistry players)
        => players.ConnectAsync(new PlayerConnection(Player, "Player", PlayerTeam.Terrorist, true, Now)).AsTask();

    private static AchievementUnlockRecord Unlock(int tier, long awardedXp)
        => new("headshots", 1, tier, new ProgressionGrantRecord(Player, $"achievement:headshots:{tier}",
            ProgressionXpSource.AchievementReward, awardedXp / 2, awardedXp, "achievement.headshots", Now,
            "double", 2, awardedXp, tier));

    private sealed class Settings : IPlayerSettingsService
    {
        public bool Enabled { get; init; } = true;
        public Func<Task>? BeforeRead { get; init; }
        public async ValueTask<T> GetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key,
            CancellationToken cancellationToken = default)
        {
            if (BeforeRead is not null) await BeforeRead();
            return (T)(object)Enabled;
        }
        public ValueTask SetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, T value,
            CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask<bool> ResetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key,
            CancellationToken cancellationToken = default) => throw new AssertFailedException();
    }

    private sealed class Messages : IMessageService
    {
        public bool FailFirst { get; init; }
        public List<MessageRequest> Requests { get; } = [];
        public ValueTask<MessageDispatchResult> SendAsync(MessageRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (FailFirst && Requests.Count == 1) throw new InvalidOperationException("transport failure");
            return ValueTask.FromResult(MessageDispatchResult.DeliveredResult);
        }
    }
}
