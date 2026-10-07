using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Placeholders;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
using AnoCore.Modules.Admin;
using AnoCore.Runtime.Configuration;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Placeholders;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class ChatFormatSnapshotLifecycleTests
{
    private static readonly PlayerId Player = new(76561198000012631);
    private static readonly DateTimeOffset Now =
        new(2026, 9, 27, 22, 0, 0, TimeSpan.Zero);
    private string _root = null!;

    [TestInitialize]
    public void Initialize() => _root = Path.Combine(Path.GetTempPath(),
        "anocore-chat-snapshot-tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task ReloadDuringPreparationCannotPublishOldOrMixedTemplates()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var store = new JsonConfigStore(_root);
        var reloads = new ConfigReloadRegistry();
        var events = new AnoEventBus();
        var placeholders = Tags(async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
            }
            return "[R]";
        });
        using var formatter = await ChatMessageFormatter.CreateAsync(store, placeholders, reloads: reloads);
        using var snapshots = new ChatFormatSnapshotLifecycle(events, formatter);
        var player = Snapshot(PlayerSessionId.New(), "Player");
        var warming = events.PublishAsync(new PlayerConnectedEvent(player)).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await store.SaveAsync("chat-format", new ChatFormatConfiguration
        {
            PublicTemplate = "NEW {player.name}: {message}",
            TeamTemplate = "TEAMNEW {player.name}: {message}",
        });
        await reloads.ReloadAsync("chat-format");
        release.SetResult();
        await warming.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsFalse(snapshots.TryFormat(Player, player.SessionId, "stale", false, out _));
        await snapshots.WarmExistingAsync([player]);
        Assert.IsTrue(snapshots.TryFormat(Player, player.SessionId, "new", true, out var message));
        Assert.AreEqual("TEAMNEW Player: new", message);
    }

    [TestMethod]
    public async Task ReloadInvalidatesCachedPolicyAndRejectsInvalidCandidates()
    {
        var store = new JsonConfigStore(_root);
        var reloads = new ConfigReloadRegistry();
        var events = new AnoEventBus();
        using var formatter = await ChatMessageFormatter.CreateAsync(store, new PlaceholderRegistry(), reloads: reloads);
        using var snapshots = new ChatFormatSnapshotLifecycle(events, formatter);
        var player = Snapshot(PlayerSessionId.New(), "Player");
        await events.PublishAsync(new PlayerConnectedEvent(player));
        Assert.IsTrue(snapshots.TryFormat(Player, player.SessionId, "old", false, out _));
        await store.SaveAsync("chat-format", new ChatFormatConfiguration
        {
            PublicTemplate = "NEW {player.name}: {message}",
            TeamTemplate = "TEAMNEW {player.name}: {message}",
        });
        await reloads.ReloadAsync("chat-format");
        Assert.IsFalse(snapshots.TryFormat(Player, player.SessionId, "stale", false, out _));
        await snapshots.WarmExistingAsync([player]);
        Assert.IsTrue(snapshots.TryFormat(Player, player.SessionId, "new", true, out var message));
        Assert.AreEqual("TEAMNEW Player: new", message);
        await store.SaveAsync("chat-format", new ChatFormatConfiguration { PublicTemplate = "invalid" });
        await Assert.ThrowsAsync<Exception>(async () => await reloads.ReloadAsync("chat-format"));
        Assert.IsTrue(snapshots.TryFormat(Player, player.SessionId, "kept", true, out message));
        Assert.AreEqual("TEAMNEW Player: kept", message);
        formatter.Dispose();
        Assert.AreEqual(0, reloads.Configurations.Count);
    }

    [TestMethod]
    public async Task TagPolicyIdentityImmediatelyInvalidatesWarmedChat()
    {
        var events = new AnoEventBus();
        using var formatter = await ChatMessageFormatter.CreateAsync(new JsonConfigStore(_root), new PlaceholderRegistry());
        object identity = new();
        formatter.TagPolicyIdentity = () => identity;
        using var snapshots = new ChatFormatSnapshotLifecycle(events, formatter);
        var player = Snapshot(PlayerSessionId.New(), "Player");
        await events.PublishAsync(new PlayerConnectedEvent(player));
        identity = new();
        Assert.IsFalse(snapshots.TryFormat(Player, player.SessionId, "stale", false, out _));
        await snapshots.WarmExistingAsync([player]);
        Assert.IsTrue(snapshots.TryFormat(Player, player.SessionId, "current", false, out _));
    }

    [TestMethod]
    public async Task ConnectedPlayer_WarmsSynchronousPublicAndTeamFormats()
    {
        var events = new AnoEventBus();
        var placeholders = Tags((_, _) => ValueTask.FromResult<string?>("[R]"));
        var formatter = await ChatMessageFormatter.CreateAsync(
            new JsonConfigStore(_root), placeholders);
        using var snapshots = new ChatFormatSnapshotLifecycle(events, formatter);
        var player = Snapshot(PlayerSessionId.New(), "Player");

        await events.PublishAsync(new PlayerConnectedEvent(player));

        Assert.IsTrue(snapshots.TryFormat(
            Player, player.SessionId, "hello", false, out var publicMessage));
        Assert.AreEqual("[R] Player: hello", publicMessage);
        Assert.IsTrue(snapshots.TryFormat(
            Player, player.SessionId, "team", true, out var teamMessage));
        Assert.AreEqual("(TEAM) [R] Player: team", teamMessage);
    }

    [TestMethod]
    public async Task ReconnectRace_DoesNotPublishStaleInflightSession()
    {
        var events = new AnoEventBus();
        var oldSession = PlayerSessionId.New();
        var currentSession = PlayerSessionId.New();
        var releaseOld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var placeholders = Tags(async (context, token) =>
        {
            if (Equals(context.Values["session"], oldSession))
                await releaseOld.Task.WaitAsync(token);
            return Equals(context.Values["session"], oldSession) ? "[OLD]" : "[NEW]";
        });
        var formatter = await ChatMessageFormatter.CreateAsync(
            new JsonConfigStore(_root), placeholders);
        using var snapshots = new ChatFormatSnapshotLifecycle(events, formatter);
        var previous = Snapshot(oldSession, "Old");
        var current = Snapshot(currentSession, "New");

        var staleWarm = events.PublishAsync(new PlayerConnectedEvent(previous)).AsTask();
        await events.PublishAsync(new PlayerReconnectedEvent(previous, current));
        releaseOld.SetResult();
        await staleWarm;

        Assert.IsFalse(snapshots.TryFormat(
            Player, oldSession, "x", false, out _));
        Assert.IsTrue(snapshots.TryFormat(
            Player, currentSession, "x", false, out var message));
        Assert.AreEqual("[NEW] New: x", message);
    }

    [TestMethod]
    public async Task StaleDisconnectAndNameUpdate_PreserveCurrentSessionAndRefreshName()
    {
        var events = new AnoEventBus();
        var formatter = await ChatMessageFormatter.CreateAsync(
            new JsonConfigStore(_root),
            Tags((_, _) => ValueTask.FromResult<string?>("[R]")));
        using var snapshots = new ChatFormatSnapshotLifecycle(events, formatter);
        var previous = Snapshot(PlayerSessionId.New(), "Old");
        var current = Snapshot(PlayerSessionId.New(), "Current");
        await events.PublishAsync(new PlayerConnectedEvent(previous));
        await events.PublishAsync(new PlayerReconnectedEvent(previous, current));

        await events.PublishAsync(new PlayerDisconnectedEvent(
            Snapshot(previous.SessionId, "Old", isConnected: false)));
        var renamed = Snapshot(current.SessionId, "Renamed");
        await events.PublishAsync(new PlayerUpdatedEvent(current, renamed));

        Assert.IsTrue(snapshots.TryFormat(
            Player, current.SessionId, "x", false, out var message));
        Assert.AreEqual("[R] Renamed: x", message);
    }

    [TestMethod]
    public async Task Bootstrap_IsolatesFailureAndWarmsRemainingPlayer()
    {
        var other = new PlayerId(76561198000012632);
        var events = new AnoEventBus();
        var placeholders = Tags((context, _) =>
            Equals(context.Values["player"], Player)
                ? ValueTask.FromException<string?>(new InvalidOperationException("boom"))
                : ValueTask.FromResult<string?>("[OK]"));
        var formatter = await ChatMessageFormatter.CreateAsync(
            new JsonConfigStore(_root), placeholders);
        var failures = new List<Exception>();
        using var snapshots = new ChatFormatSnapshotLifecycle(
            events, formatter, failures.Add);
        var failed = Snapshot(PlayerSessionId.New(), "Failed");
        var healthy = Snapshot(PlayerSessionId.New(), "Healthy", id: other);

        await snapshots.WarmExistingAsync([failed, healthy]);

        Assert.AreEqual(1, failures.Count);
        Assert.IsFalse(snapshots.TryFormat(
            Player, failed.SessionId, "x", false, out _));
        Assert.IsTrue(snapshots.TryFormat(
            other, healthy.SessionId, "x", false, out var message));
        Assert.AreEqual("[OK] Healthy: x", message);
    }

    [TestMethod]
    public async Task ExplicitRefresh_RebuildsCurrentSessionPlaceholders()
    {
        var events = new AnoEventBus();
        var tag = "[OLD]";
        var formatter = await ChatMessageFormatter.CreateAsync(
            new JsonConfigStore(_root),
            Tags((_, _) => ValueTask.FromResult<string?>(tag)));
        using var snapshots = new ChatFormatSnapshotLifecycle(events, formatter);
        var player = Snapshot(PlayerSessionId.New(), "Player");
        await events.PublishAsync(new PlayerConnectedEvent(player));
        tag = "[NEW]";

        await snapshots.RefreshAsync(player);

        Assert.IsTrue(snapshots.TryFormat(
            Player, player.SessionId, "hello", false, out var message));
        Assert.AreEqual("[NEW] Player: hello", message);
    }

    [TestMethod]
    public async Task TagPolicyRefreshFailureInvalidatesOldAuthorizedTag()
    {
        var events = new AnoEventBus();
        var permissionStoreFails = false;
        var formatter = await ChatMessageFormatter.CreateAsync(
            new JsonConfigStore(_root),
            Tags((_, _) => permissionStoreFails
                ? ValueTask.FromException<string?>(
                    new InvalidOperationException("permission read failed"))
                : ValueTask.FromResult<string?>("[Staff]")));
        using var snapshots = new ChatFormatSnapshotLifecycle(events, formatter);
        var player = Snapshot(PlayerSessionId.New(), "Player");
        await events.PublishAsync(new PlayerConnectedEvent(player));
        Assert.IsTrue(snapshots.TryFormat(
            Player, player.SessionId, "before", false, out _));

        permissionStoreFails = true;
        await snapshots.RefreshTagPolicyAsync(player);

        Assert.IsFalse(snapshots.TryFormat(
            Player, player.SessionId, "after", false, out _));
    }

    [TestMethod]
    public async Task DisconnectAndDispose_ClearSnapshotsAndUnsubscribe()
    {
        var events = new AnoEventBus();
        var formatter = await ChatMessageFormatter.CreateAsync(
            new JsonConfigStore(_root),
            Tags((_, _) => ValueTask.FromResult<string?>("[R]")));
        var snapshots = new ChatFormatSnapshotLifecycle(events, formatter);
        var player = Snapshot(PlayerSessionId.New(), "Player");
        await events.PublishAsync(new PlayerConnectedEvent(player));

        await events.PublishAsync(new PlayerDisconnectedEvent(
            Snapshot(player.SessionId, "Player", isConnected: false)));
        Assert.IsFalse(snapshots.TryFormat(
            Player, player.SessionId, "x", false, out _));

        snapshots.Dispose();
        await events.PublishAsync(new PlayerConnectedEvent(
            Snapshot(PlayerSessionId.New(), "Later")));
        Assert.IsFalse(snapshots.TryFormat(
            Player, player.SessionId, "x", false, out _));
    }

    [TestMethod]
    public async Task Bootstrap_PropagatesRequestedCancellation()
    {
        var events = new AnoEventBus();
        var placeholders = Tags(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return "[R]";
        });
        var formatter = await ChatMessageFormatter.CreateAsync(
            new JsonConfigStore(_root), placeholders);
        using var snapshots = new ChatFormatSnapshotLifecycle(events, formatter);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await snapshots.WarmExistingAsync(
                [Snapshot(PlayerSessionId.New(), "Player")], cancellation.Token));
    }

    private static PlaceholderRegistry Tags(PlaceholderResolver resolver)
    {
        var placeholders = new PlaceholderRegistry();
        placeholders.Register(new ModuleId("test"), "rank.tag", resolver);
        return placeholders;
    }

    private static PlayerSnapshot Snapshot(
        PlayerSessionId sessionId,
        string name,
        bool isConnected = true,
        PlayerId? id = null)
        => new(
            id ?? Player,
            sessionId,
            name,
            isConnected,
            isAlive: isConnected,
            PlayerTeam.CounterTerrorist,
            Now,
            Now);
}
