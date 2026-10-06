using AnoCore.Abstractions.Messaging;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;
using AnoCore.Runtime.Settings;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class PlaytimeNotificationServiceTests
{
    private static readonly PlayerId Player = new(76561198000022601);
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);
    private string _root = null!;

    [TestInitialize]
    public void Initialize() => _root = Path.Combine(Path.GetTempPath(),
        "anocore-playtime-notification-tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task Tick_WaitsFullIntervalAndSendsCurrentSessionTotalsOnlyOnce()
    {
        using var test = await CreateAsync();
        await test.Service.TickAsync(Start.AddSeconds(299));
        Assert.AreEqual(0, test.Settings.Reads);
        await test.Service.TickAsync(Start.AddSeconds(300));
        await test.Service.TickAsync(Start.AddSeconds(300));

        var message = test.Messages.Sent.Single();
        Assert.AreEqual(Player, message.Target.PlayerId);
        Assert.AreEqual(test.Session, message.Target.SessionId);
        Assert.AreEqual(MessageChannel.Chat, message.Channel);
        StringAssert.Contains(message.Text, "02:00:00");
        Assert.AreEqual(1, test.Repository.Reads);
        await test.Service.TickAsync(Start.AddSeconds(600));
        Assert.AreEqual(2, test.Messages.Sent.Count);
    }

    [TestMethod]
    public async Task DisabledPreferenceSkipsStorageAndCanBeReenabled()
    {
        using var test = await CreateAsync();
        test.Settings.Enabled = false;
        await test.Service.TickAsync(Start.AddSeconds(300));
        Assert.AreEqual(0, test.Repository.Reads);
        Assert.AreEqual(0, test.Messages.Sent.Count);
        test.Settings.Enabled = true;
        await test.Service.TickAsync(Start.AddSeconds(600));
        Assert.AreEqual(1, test.Messages.Sent.Count);
    }

    [TestMethod]
    public async Task GlobalDisableSkipsAllReadsButKeepsPreferenceRegistered()
    {
        using var test = await CreateAsync(enabled: false);
        await test.Service.TickAsync(Start.AddHours(1));
        Assert.AreEqual(0, test.Settings.Reads);
        Assert.IsTrue(test.Catalog.TryGet(PlaytimeNotificationService.Preference.Name, out var setting));
        Assert.IsTrue(setting!.Key.DefaultValue);
    }

    [TestMethod]
    public async Task ReconnectDuringPreferenceReadCannotSendToNewSession()
    {
        using var test = await CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Settings.Read = async token =>
        {
            started.SetResult();
            await release.Task.WaitAsync(token);
            return true;
        };
        var tick = test.Service.TickAsync(Start.AddMinutes(5)).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newSession = await test.Players.ConnectAsync(new PlayerConnection(
            Player, "Reconnected", PlayerTeam.Terrorist, true, Start.AddMinutes(5)));
        release.SetResult();
        await tick;
        Assert.AreEqual(0, test.Messages.Sent.Count);
        test.Settings.Read = null;
        await test.Service.TickAsync(Start.AddMinutes(9));
        Assert.AreEqual(0, test.Messages.Sent.Count);
        await test.Service.TickAsync(Start.AddMinutes(10));
        Assert.AreEqual(newSession.SessionId, test.Messages.Sent.Single().Target.SessionId);
    }

    [TestMethod]
    public async Task ConcurrentTicksDoNotDuplicateMessages()
    {
        using var test = await CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Settings.Read = async token =>
        {
            started.SetResult();
            await release.Task.WaitAsync(token);
            return true;
        };
        var first = test.Service.TickAsync(Start.AddMinutes(5)).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await test.Service.TickAsync(Start.AddMinutes(5));
        release.SetResult();
        await first;
        Assert.AreEqual(1, test.Messages.Sent.Count);
    }

    [TestMethod]
    public async Task PreferenceFailureIsIsolatedAndNotRetriedEachHeartbeat()
    {
        using var test = await CreateAsync();
        test.Settings.Read = _ => ValueTask.FromException<bool>(new InvalidOperationException("unavailable"));
        await test.Service.TickAsync(Start.AddMinutes(5));
        await test.Service.TickAsync(Start.AddMinutes(5).AddSeconds(5));
        Assert.AreEqual(1, test.Failures.Count);
        Assert.AreEqual(0, test.Messages.Sent.Count);
        test.Settings.Read = null;
        await test.Service.TickAsync(Start.AddMinutes(10));
        Assert.AreEqual(1, test.Messages.Sent.Count);
    }

    [TestMethod]
    public async Task DisposeCancelsInflightReadAndRemovesToggle()
    {
        using var test = await CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Settings.Read = async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        };
        var tick = test.Service.TickAsync(Start.AddMinutes(5)).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        test.Service.Dispose();
        await tick.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(test.Catalog.TryGet(PlaytimeNotificationService.Preference.Name, out _));
        Assert.AreEqual(0, test.Messages.Sent.Count);
        await test.Service.TickAsync(Start.AddMinutes(10));
    }

    [TestMethod]
    public void ConfigurationRejectsIntervalsOutsideBounds()
    {
        Assert.AreEqual(0, PlaytimeNotificationConfiguration.Validate(new()).Count);
        Assert.IsTrue(PlaytimeNotificationConfiguration.Validate(new() { IntervalSeconds = 29 }).Count > 0);
        Assert.IsTrue(PlaytimeNotificationConfiguration.Validate(new() { IntervalSeconds = 86401 }).Count > 0);
    }

    private async Task<Harness> CreateAsync(bool enabled = true)
    {
        var configuration = new JsonConfigStore(_root);
        await configuration.SaveAsync("playtime-notifications",
            new PlaytimeNotificationConfiguration { Enabled = enabled });
        var players = new PlayerRegistry(new AnoEventBus());
        var player = await players.ConnectAsync(new PlayerConnection(
            Player, "Player", PlayerTeam.CounterTerrorist, true, Start));
        var repository = new Repository();
        var settings = new Settings();
        var catalog = new PlayerToggleCatalog();
        var messages = new Messages();
        var failures = new List<Exception>();
        var service = await PlaytimeNotificationService.CreateAsync(
            configuration, players, repository, settings, catalog, messages, failures.Add);
        return new Harness(service, players, player.SessionId, repository, settings, catalog, messages, failures);
    }

    private sealed record Harness(PlaytimeNotificationService Service,
        PlayerRegistry Players, PlayerSessionId Session, Repository Repository,
        Settings Settings, PlayerToggleCatalog Catalog, Messages Messages,
        List<Exception> Failures) : IDisposable
    {
        public void Dispose() => Service.Dispose();
    }

    private sealed class Settings : IPlayerSettingsService
    {
        public bool Enabled { get; set; } = true;
        public int Reads { get; private set; }
        public Func<CancellationToken, ValueTask<bool>>? Read { get; set; }
        public async ValueTask<T> GetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            Assert.AreEqual(PlaytimeNotificationService.Preference.Name, key.Name);
            return (T)(object)(Read is null ? Enabled : await Read(cancellationToken));
        }
        public ValueTask SetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, T value,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> ResetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Messages : IMessageService
    {
        public List<MessageRequest> Sent { get; } = [];
        public ValueTask<MessageDispatchResult> SendAsync(MessageRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent.Add(request);
            return ValueTask.FromResult(MessageDispatchResult.DeliveredResult);
        }
    }

    private sealed class Repository : IPlaytimeRepository
    {
        public int Reads { get; private set; }
        public ValueTask OpenAsync(PlayerId playerId, PlayerSessionId sessionId, DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AdvanceAsync(PlayerId playerId, PlayerSessionId sessionId, DateTimeOffset atUtc,
            bool close = false, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<PlaytimeTotals> ReadAsync(PlayerId playerId, DateOnly utcDay,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(new PlaytimeTotals(TimeSpan.FromHours(2), TimeSpan.FromMinutes(5)));
        }
        public ValueTask<IReadOnlyList<PlaytimeRankEntry>> GetTopAsync(int limit, int offset,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<PlaytimeRankEntry>>([]);
    }
}
