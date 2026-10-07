using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Settings;
using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class RankNotificationPreferenceSinkTests
{
    private static readonly PlayerId Player = new(76561198000017101);
    private static readonly RankTransition Transition = new(
        RankTransitionKind.Promotion,
        new RankThreshold("Recruit", 0),
        new RankThreshold("Veteran", 10),
        9,
        11);

    [TestMethod]
    public async Task NotifyAsync_DefaultPreference_ForwardsTransition()
    {
        var settings = new FakeSettings();
        var inner = new RecordingSink();
        using var sink = new RankNotificationPreferenceSink(settings, inner);

        await sink.NotifyAsync(Player, Transition);

        Assert.AreEqual(1, inner.Notifications.Count);
        Assert.AreEqual(Player, inner.Notifications[0].PlayerId);
        Assert.AreSame(Transition, inner.Notifications[0].Transition);
        Assert.AreEqual(RankNotificationPreferenceSink.EnabledSetting.Name, settings.LastKey);
    }

    [TestMethod]
    public async Task NotifyAsync_DisabledPreference_SuppressesTransition()
    {
        var settings = new FakeSettings(enabled: false);
        var inner = new RecordingSink();
        using var sink = new RankNotificationPreferenceSink(settings, inner);

        await sink.NotifyAsync(Player, Transition);

        Assert.AreEqual(0, inner.Notifications.Count);
        Assert.AreEqual(RankNotificationPreferenceSink.EnabledSetting.Name, settings.LastKey);
    }

    [TestMethod]
    public async Task NotifyAsync_CancelledPreferenceRead_DoesNotForward()
    {
        var settings = new FakeSettings();
        var inner = new RecordingSink();
        using var sink = new RankNotificationPreferenceSink(settings, inner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await sink.NotifyAsync(Player, Transition, cancellation.Token).AsTask());

        Assert.AreEqual(0, inner.Notifications.Count);
    }

    [TestMethod]
    public async Task NotifyAsync_PreferenceReadFailure_IsolatedAndReported()
    {
        var failure = new InvalidOperationException("settings unavailable");
        var settings = new ThrowingSettings(failure);
        var inner = new RecordingSink();
        Exception? reported = null;
        using var sink = new RankNotificationPreferenceSink(
            settings, inner, exception => reported = exception);

        await sink.NotifyAsync(Player, Transition);

        Assert.AreEqual(0, inner.Notifications.Count);
        Assert.AreSame(failure, reported);
    }

    [TestMethod]
    public void Dispose_DisposesOwnedNotificationSink()
    {
        var inner = new RecordingSink();
        var sink = new RankNotificationPreferenceSink(new FakeSettings(), inner);

        sink.Dispose();
        sink.Dispose();

        Assert.AreEqual(1, inner.Disposals);
    }

    [TestMethod]
    public async Task SessionNotice_PreservesCapturedIdentityAndDisposeSuppressesPendingPreferenceRead()
    {
        var at = DateTimeOffset.UtcNow;
        var player = new PlayerSnapshot(Player, PlayerSessionId.New(), "Player", true, true,
            PlayerTeam.Terrorist, at, at);
        var inner = new SessionSink();
        using (var sink = new RankNotificationPreferenceSink(new FakeSettings(), inner))
            await sink.NotifyAsync(player, Transition);
        Assert.AreSame(player, inner.Captured);
        var settings = new DeferredSettings();
        var pendingInner = new SessionSink();
        var pendingSink = new RankNotificationPreferenceSink(settings, pendingInner);
        var pending = pendingSink.NotifyAsync(player, Transition).AsTask();
        await settings.Started.Task;
        pendingSink.Dispose();
        settings.Release.SetResult();
        await pending;
        Assert.IsNull(pendingInner.Captured);
    }

    private sealed class SessionSink : ISessionRankTransitionNotificationSink
    {
        public PlayerSnapshot? Captured { get; private set; }
        public ValueTask NotifyAsync(PlayerId id, RankTransition transition, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("The session-pinned overload must be used.");
        public ValueTask NotifyAsync(PlayerSnapshot player, RankTransition transition, CancellationToken cancellationToken = default)
        {
            Captured = player;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DeferredSettings : IPlayerSettingsService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<T> GetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Release.Task;
            return key.DefaultValue;
        }
        public ValueTask SetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, T value, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask<bool> ResetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, CancellationToken cancellationToken = default) => throw new AssertFailedException();
    }

    private sealed class FakeSettings(bool enabled = true) : IPlayerSettingsService
    {
        public string? LastKey { get; private set; }

        public ValueTask<T> GetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastKey = key.Name;
            Assert.AreEqual(Player, playerId);
            Assert.AreEqual(typeof(bool), typeof(T));
            return ValueTask.FromResult((T)(object)enabled);
        }

        public ValueTask SetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, T value,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<bool> ResetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingSettings(Exception failure) : IPlayerSettingsService
    {
        public ValueTask<T> GetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromException<T>(failure);
        }

        public ValueTask SetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key, T value,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<bool> ResetAsync<T>(PlayerId playerId, PlayerSettingKey<T> key,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingSink : IRankTransitionNotificationSink, IDisposable
    {
        public List<(PlayerId PlayerId, RankTransition Transition)> Notifications { get; } = [];
        public int Disposals { get; private set; }

        public ValueTask NotifyAsync(PlayerId playerId, RankTransition transition,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Notifications.Add((playerId, transition));
            return ValueTask.CompletedTask;
        }

        public void Dispose() => Disposals++;
    }
}
