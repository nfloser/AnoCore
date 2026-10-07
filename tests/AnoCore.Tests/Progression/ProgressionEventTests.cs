using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Progression;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Runtime.Settings;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class ProgressionEventTests
{
    private static readonly PlayerId Player = new(76561198000291101);
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 13, 0, 0, TimeSpan.Zero);
    private static ProgressionDefinitionSnapshot Xp => ProgressionDefinitionSnapshot.Create(
        [new(1, 0), new(2, 100), new(3, 300)], []);

    [TestMethod]
    public async Task Grant_DerivesExactAndMultiLevelTransitionsFromCommittedTotals()
    {
        var events = new AnoEventBus();
        var player = await Connect(new PlayerRegistry(events));
        var grants = new List<ProgressionXpGrantedEvent>();
        var levels = new List<ProgressionLevelUpEvent>();
        using var g = events.Subscribe<ProgressionXpGrantedEvent>((value, _) => { grants.Add(value); return ValueTask.CompletedTask; });
        using var l = events.Subscribe<ProgressionLevelUpEvent>((value, _) => { levels.Add(value); return ValueTask.CompletedTask; });
        var publisher = new ProgressionEventPublisher(events, Xp);
        await publisher.GrantAsync(player, Grant(10, 99));
        await publisher.GrantAsync(player, Grant(1, 100));
        await publisher.GrantAsync(player, Grant(300, 300));
        await publisher.GrantAsync(player, Grant(0, 300));
        Assert.HasCount(4, grants);
        Assert.HasCount(2, levels);
        Assert.AreEqual(1, levels[0].PreviousLevel);
        Assert.AreEqual(2, levels[0].Level);
        Assert.AreEqual(1, levels[1].PreviousLevel);
        Assert.AreEqual(3, levels[1].Level);
    }

    [TestMethod]
    public async Task Challenge_ReplayAndEmptyResultEmitNothing()
    {
        var events = new AnoEventBus();
        var player = await Connect(new PlayerRegistry(events));
        var count = 0;
        using var subscription = events.Subscribe<ChallengeCompletedEvent>((value, _) =>
        {
            Assert.AreEqual("weekly.wins", value.Completion.ChallengeId);
            count++;
            return ValueTask.CompletedTask;
        });
        var publisher = new ProgressionEventPublisher(events, Xp);
        await publisher.ChallengeAsync(player, Completion(true));
        await publisher.ChallengeAsync(player, Completion(false));
        await publisher.ChallengeAsync(player, new(true, Completion(true).Evaluation, null));
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task ObserverFailure_DoesNotSuppressLaterEventsOrObservers()
    {
        var events = new AnoEventBus();
        var player = await Connect(new PlayerRegistry(events));
        var seen = new List<string>();
        using var bad = events.Subscribe<ProgressionXpGrantedEvent>((_, _) => throw new InvalidOperationException("observer"));
        using var good = events.Subscribe<ProgressionXpGrantedEvent>((_, _) => { seen.Add("xp"); return ValueTask.CompletedTask; });
        using var level = events.Subscribe<ProgressionLevelUpEvent>((_, _) => { seen.Add("level"); return ValueTask.CompletedTask; });
        using var achievement = events.Subscribe<AchievementUnlockedEvent>((_, _) => { seen.Add("achievement"); return ValueTask.CompletedTask; });
        var publisher = new ProgressionEventPublisher(events, Xp, _ => throw new InvalidOperationException("diagnostic"));
        await publisher.AchievementAsync(player, new("wins", 1, 1, Grant(100, 100)));
        CollectionAssert.AreEqual(new[] { "xp", "level", "achievement" }, seen);
    }

    [TestMethod]
    public async Task InvalidCommittedTotalsAndPlayerAreRejectedBeforePublication()
    {
        var events = new AnoEventBus();
        var player = await Connect(new PlayerRegistry(events));
        var publisher = new ProgressionEventPublisher(events, Xp);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => publisher.GrantAsync(player, Grant(100, 50)).AsTask());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => publisher.GrantAsync(player, Grant(-1, 100)).AsTask());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => publisher.GrantAsync(player,
            Grant(100, 100) with { PlayerId = new(Player.SteamId64 + 1) }).AsTask());
    }

    [TestMethod]
    public async Task LevelNotice_IsPinnedAndToggleAndSubscriptionAreOwned()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var player = await Connect(players);
        var messages = new Messages();
        var toggles = new PlayerToggleCatalog();
        using var notices = new LevelNotificationService(events, players, new Settings(), toggles, messages);
        var publisher = new ProgressionEventPublisher(events, Xp);
        await publisher.GrantAsync(player, Grant(300, 300));
        Assert.HasCount(1, messages.Requests);
        Assert.AreEqual(player.SessionId, messages.Requests[0].Target.SessionId);
        StringAssert.Contains(messages.Requests[0].Text, "1 -> 3");
        Assert.IsTrue(toggles.TryGet(LevelNotificationService.Preference.Name, out _));
        notices.Dispose();
        Assert.IsFalse(toggles.TryGet(LevelNotificationService.Preference.Name, out _));
        await publisher.GrantAsync(player, Grant(100, 100));
        Assert.HasCount(1, messages.Requests);
    }

    [TestMethod]
    public async Task DisabledNoticeDoesNotSuppressCommittedEvents()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var player = await Connect(players);
        var messages = new Messages();
        var completed = 0;
        using var s = events.Subscribe<ChallengeCompletedEvent>((_, _) => { completed++; return ValueTask.CompletedTask; });
        using var notices = new LevelNotificationService(events, players, new Settings { Enabled = false }, new PlayerToggleCatalog(), messages);
        await new ProgressionEventPublisher(events, Xp).ChallengeAsync(player, Completion(true));
        Assert.IsEmpty(messages.Requests);
        Assert.AreEqual(1, completed);
    }

    [TestMethod]
    public async Task ReconnectAndUnloadDuringPreferenceReadSuppressDelivery()
    {
        foreach (var reconnect in new[] { true, false })
        {
            var events = new AnoEventBus();
            var players = new PlayerRegistry(events);
            var player = await Connect(players);
            var messages = new Messages();
            LevelNotificationService? notices = null;
            var settings = new Settings { BeforeRead = async () => { if (reconnect) await Connect(players); else notices!.Dispose(); } };
            using (notices = new LevelNotificationService(events, players, settings, new PlayerToggleCatalog(), messages))
                await new ProgressionEventPublisher(events, Xp).GrantAsync(player, Grant(100, 100));
            Assert.IsEmpty(messages.Requests);
        }
    }

    [TestMethod]
    public async Task SettingsAndTransportFailuresRemainBestEffort()
    {
        foreach (var settingsFail in new[] { true, false })
        {
            var events = new AnoEventBus();
            var players = new PlayerRegistry(events);
            var player = await Connect(players);
            var messages = new Messages { Fail = !settingsFail };
            var settings = new Settings { BeforeRead = settingsFail ? () => throw new InvalidOperationException("settings") : null };
            using var notices = new LevelNotificationService(events, players, settings, new PlayerToggleCatalog(), messages,
                _ => throw new InvalidOperationException("diagnostics"));
            await new ProgressionEventPublisher(events, Xp).GrantAsync(player, Grant(100, 100));
            Assert.HasCount(settingsFail ? 0 : 1, messages.Requests);
        }
    }

    [TestMethod]
    public async Task SubscriptionFailureRollsBackToggleAndCancelledPublishStops()
    {
        var events = new AnoEventBus();
        var players = new PlayerRegistry(events);
        var player = await Connect(players);
        var toggles = new PlayerToggleCatalog();
        Assert.ThrowsExactly<InvalidOperationException>(() => new LevelNotificationService(new RejectSubscription(), players,
            new Settings(), toggles, new Messages()));
        Assert.IsFalse(toggles.TryGet(LevelNotificationService.Preference.Name, out _));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new ProgressionEventPublisher(events, Xp)
            .GrantAsync(player, Grant(100, 100), cancellation.Token).AsTask());
    }

    private static Task<PlayerSnapshot> Connect(PlayerRegistry players)
        => players.ConnectAsync(new(Player, "Player", PlayerTeam.Terrorist, true, Now)).AsTask();

    private static ProgressionGrantRecord Grant(long amount, long total)
        => new(Player, "test:grant", ProgressionXpSource.Gameplay, amount, amount, "test", Now, null, 1m, total, 1);

    private static ChallengeCompletionResult Completion(bool applied)
    {
        var definition = new ChallengeDefinition("weekly.wins", 1, "Weekly wins", ChallengeWindowKind.Weekly,
            GameplayStatKind.RoundWon, 10, 100, Now, Now.AddDays(7), []);
        return new(applied, new(definition, ChallengeEvaluationState.Completed, 10, 10, false, []),
            new(definition.Id, 1, Now, Now.AddDays(7), Grant(100, 100)));
    }

    private sealed class Settings : IPlayerSettingsService
    {
        public bool Enabled { get; init; } = true;
        public Func<Task>? BeforeRead { get; init; }
        public async ValueTask<T> GetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, CancellationToken cancellationToken = default)
        {
            if (BeforeRead is not null) await BeforeRead();
            return (T)(object)Enabled;
        }
        public ValueTask SetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, T value, CancellationToken cancellationToken = default)
            => throw new AssertFailedException();
        public ValueTask<bool> ResetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, CancellationToken cancellationToken = default)
            => throw new AssertFailedException();
    }

    private sealed class Messages : IMessageService
    {
        public bool Fail { get; init; }
        public List<MessageRequest> Requests { get; } = [];
        public ValueTask<MessageDispatchResult> SendAsync(MessageRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Fail) throw new InvalidOperationException("transport");
            return ValueTask.FromResult(MessageDispatchResult.DeliveredResult);
        }
    }

    private sealed class RejectSubscription : IAnoEventBus
    {
        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler) where TEvent : IAnoEvent
            => throw new InvalidOperationException("subscription");
        public ValueTask PublishAsync<TEvent>(TEvent value, CancellationToken cancellationToken = default) where TEvent : IAnoEvent
            => throw new AssertFailedException();
    }
}
