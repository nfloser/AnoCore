using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Modules.Progression;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Runtime.Settings;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class ChallengeNotificationTests
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
        using var service = new ChallengeNotificationService(players, new Settings(), toggles, messages);
        await service.NotifyAsync(player, "Weekly wins", Completion(true, 200));
        await service.NotifyAsync(player, "Weekly wins", Completion(false, 200));
        Assert.HasCount(1, messages.Requests);
        StringAssert.Contains(messages.Requests[0].Text, "+200 XP");
        Assert.AreEqual(player.SessionId, messages.Requests[0].Target.SessionId);
        Assert.AreEqual(Player, messages.Requests[0].Target.PlayerId);
        Assert.IsTrue(toggles.TryGet(PreferenceName, out _));
        service.Dispose();
        Assert.IsFalse(toggles.TryGet(PreferenceName, out _));
        await service.NotifyAsync(player, "Weekly wins", Completion(true, 400));
        Assert.HasCount(1, messages.Requests);
    }

    [TestMethod]
    public async Task DisabledPreference_SuppressesChat()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var player = await Connect(players);
        var messages = new Messages();
        using var service = new ChallengeNotificationService(players, new Settings { Enabled = false },
            new PlayerToggleCatalog(), messages);
        await service.NotifyAsync(player, "Weekly wins", Completion(true, 100));
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
        using var service = new ChallengeNotificationService(players, settings, new PlayerToggleCatalog(), messages);
        await service.NotifyAsync(player, "Weekly wins", Completion(true, 100));
        Assert.IsEmpty(messages.Requests);
    }

    [TestMethod]
    public async Task DeliveryFailure_IsBestEffortAndDiagnosticsCannotEscape()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var player = await Connect(players);
        var messages = new Messages { FailFirst = true };
        using var service = new ChallengeNotificationService(players, new Settings(), new PlayerToggleCatalog(),
            messages, _ => throw new InvalidOperationException("diagnostic failure"));
        await service.NotifyAsync(player, "{Weekly wins}|", Completion(true, 100));
        await service.NotifyAsync(player, "{Weekly wins}|", Completion(true, 200));
        Assert.HasCount(2, messages.Requests);
        StringAssert.Contains(messages.Requests[1].Text, "+200 XP");
        Assert.IsFalse(messages.Requests[1].Text.Contains('{'));
    }

    [TestMethod]
    public async Task Module_OnlyNotifiesNewCommitsAndOwnsToggleOnRegistrationFailure()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await Connect(players);
        var messages = new Messages();
        var toggles = new PlayerToggleCatalog();
        var commands = new AnoCore.Runtime.Commands.CommandRegistry(new AllowAll());
        var repository = new Repository();
        using var module = new ChallengeModule(new ChallengeConfiguration().Snapshot(),
            ProgressionDefinitionSnapshot.Create([new(1, 0)], []), players, repository, commands,
            settings: new Settings(), toggles: toggles, messages: messages);
        await module.ReconcileOnlineAsync(Now);
        await module.ReconcileOnlineAsync(Now);
        Assert.HasCount(3, messages.Requests);
        Assert.ThrowsExactly<InvalidOperationException>(() => new ChallengeModule(new ChallengeConfiguration().Snapshot(),
            ProgressionDefinitionSnapshot.Create([new(1, 0)], []), players, repository, commands,
            settings: new Settings(), toggles: new PlayerToggleCatalog(), messages: messages));
        Assert.IsTrue(toggles.TryGet(PreferenceName, out _));
        module.Dispose();
        Assert.IsFalse(toggles.TryGet(PreferenceName, out _));
    }

    [TestMethod]
    public async Task SettingsFailureAndCancellation_DoNotDispatch()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        var player = await Connect(players);
        var messages = new Messages();
        using var service = new ChallengeNotificationService(players,
            new Settings { BeforeRead = () => throw new InvalidOperationException("settings unavailable") },
            new PlayerToggleCatalog(), messages);
        await service.NotifyAsync(player, "Weekly wins", Completion(true, 100));
        Assert.IsEmpty(messages.Requests);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var cancelledService = new ChallengeNotificationService(players, new Settings(),
            new PlayerToggleCatalog(), messages);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await cancelledService.NotifyAsync(player, "Weekly wins", Completion(true, 100), cancellation.Token));
        Assert.IsEmpty(messages.Requests);
    }

    private sealed class Repository : IChallengeRepository
    {
        private readonly HashSet<string> _completed = [];
        public ValueTask<ChallengeEvaluation> ReadAsync(PlayerId playerId, ChallengeCatalogSnapshot catalog,
            string challengeId, DateTimeOffset at, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(catalog.Evaluate(challengeId, [], [], at));
        public ValueTask<ChallengeCompletionResult> CompleteAsync(PlayerId playerId, ChallengeCatalogSnapshot catalog,
            string challengeId, DateTimeOffset at, ProgressionDefinitionSnapshot xpDefinitions,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Completion(_completed.Add(challengeId), 200));
    }

    private sealed class AllowAll : AnoCore.Abstractions.Permissions.IPermissionEvaluator
    {
        public ValueTask<bool> HasPermissionAsync(PlayerId id, AnoCore.Abstractions.Permissions.PermissionId permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private static string PreferenceName => ChallengeNotificationService.Preference.Name;

    private static Task<PlayerSnapshot> Connect(PlayerRegistry players)
        => players.ConnectAsync(new PlayerConnection(Player, "Player", PlayerTeam.Terrorist, true, Now)).AsTask();

    private static ChallengeCompletionResult Completion(bool applied, long awardedXp)
    {
        var definition = new ChallengeDefinition("wins", 1, "Weekly wins", ChallengeWindowKind.Weekly,
            AnoCore.Abstractions.Stats.GameplayStatKind.RoundWon, 10, awardedXp / 2, Now, Now.AddDays(7), []);
        var grant = new ProgressionGrantRecord(Player, "challenge:wins:1", ProgressionXpSource.ChallengeReward,
            awardedXp / 2, awardedXp, "challenge.wins", Now, "double", 2, awardedXp, 1);
        return new(applied, new(definition, ChallengeEvaluationState.Completed, 10, 10, false, []),
            new("wins", 1, Now, Now.AddDays(7), grant));
    }

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
