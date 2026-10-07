using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;
using AnoCore.Runtime.Events;
using AnoCore.Runtime.Players;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class LiveRankScoringServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly PlayerId Attacker = new(76561198000280102);
    private static readonly PlayerId Victim = new(76561198000280101);

    [TestMethod]
    public async Task Replays_DoNotRecalculatePointsOrAdvanceKillstreakAndFailureCanRetry()
    {
        var setup = await Setup();
        using var service = Service(setup);
        var first = Death(setup.Players);
        setup.Events.Fail = true;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await service.RecordDeathAsync(first));
        setup.Events.Fail = false;
        await service.RecordDeathAsync(first);
        await service.RecordDeathAsync(Death(setup.Players, Now.AddSeconds(1)));
        Assert.AreEqual(9L, setup.Events.Points(Attacker));
        await service.RecordDeathAsync(first);
        await service.RecordDeathAsync(Death(setup.Players, Now.AddSeconds(2)));
        Assert.AreEqual(11L, setup.Events.Points(Attacker));
        Assert.AreEqual(3, setup.Events.Batches.Count);
        Assert.AreEqual(6, setup.Sinks.Changes.Count);
    }

    [TestMethod]
    public async Task Streaks_ResetAtTimeBoundaryRoundChangeAndReconnect()
    {
        var setup = await Setup();
        using var service = Service(setup);
        await service.RecordDeathAsync(Death(setup.Players));
        await service.RecordDeathAsync(Death(setup.Players, Now.AddSeconds(30)));
        Assert.AreEqual(4L, setup.Events.Points(Attacker));
        await service.RecordDeathAsync(Death(setup.Players, Now.AddSeconds(31)));
        Assert.AreEqual(11L, setup.Events.Points(Attacker));
        var nextRound = Death(setup.Players, Now.AddSeconds(32));
        await service.RecordDeathAsync(nextRound with { Context = nextRound.Context with { RoundKey = "round-2" } });
        Assert.AreEqual(13L, setup.Events.Points(Attacker));
        setup.Players.TryGet(Attacker, out var previous);
        await setup.Players.DisconnectAsync(Attacker, previous!.SessionId, Now.AddSeconds(33));
        await setup.Players.ConnectAsync(new PlayerConnection(Attacker, "Reconnected", PlayerTeam.Terrorist, true, Now.AddSeconds(33)));
        var reconnect = Death(setup.Players, Now.AddSeconds(34));
        await service.RecordDeathAsync(reconnect with { Context = reconnect.Context with { RoundKey = "round-2" } });
        Assert.AreEqual(15L, setup.Events.Points(Attacker));
    }

    [TestMethod]
    public async Task Gameplay_IsIndependentIdempotentAndPresentationFailureCannotUndoCommittedAwards()
    {
        var setup = await Setup();
        setup.Sinks.Fail = true;
        using var service = Service(setup, vip: 2);
        setup.Players.TryGet(Attacker, out var player);
        var statistic = new GameplayStatEvent(Guid.NewGuid(), Attacker, Now, "de_test", GameplayStatKind.BombPlanted);
        var context = new RankLiveContext(statistic.EventId, Now, false, 4, "round-1");
        await service.RecordGameplayAsync(statistic, context, player!);
        await service.RecordGameplayAsync(statistic, context, player!);
        Assert.AreEqual(10L, setup.Events.Points(Attacker));
        Assert.AreEqual(1, setup.Events.Batches.Count);
        Assert.IsTrue(setup.Errors.Count > 0);
        service.Dispose();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await service.RecordGameplayAsync(statistic, context, player!));
    }

    [TestMethod]
    public async Task ReconnectDuringPersistence_SuppressesOldSessionNotificationsButRetainsEarnedPoints()
    {
        var setup = await Setup();
        setup.Events.AfterWrite = async () =>
        {
            setup.Players.TryGet(Attacker, out var previous);
            await setup.Players.DisconnectAsync(Attacker, previous!.SessionId, Now);
            await setup.Players.ConnectAsync(new PlayerConnection(Attacker, "New session", PlayerTeam.Terrorist, true, Now));
        };
        using var service = Service(setup);
        await service.RecordDeathAsync(Death(setup.Players));
        Assert.AreEqual(2L, setup.Events.Points(Attacker));
        Assert.IsFalse(setup.Sinks.Changes.Contains(Attacker));
        Assert.IsFalse(setup.Sinks.Notices.Contains(Attacker));
    }

    [TestMethod]
    public async Task IneligibleWarmup_DoesNotWriteAndVipPermissionFailureFallsBackToBaseAward()
    {
        var setup = await Setup();
        setup.Permissions.Fail = true;
        using var service = Service(setup, vip: 2);
        var input = Death(setup.Players);
        await service.RecordDeathAsync(input with { Context = input.Context with { IsWarmup = true } });
        Assert.AreEqual(0, setup.Events.Batches.Count);
        await service.RecordDeathAsync(input);
        Assert.AreEqual(2L, setup.Events.Points(Attacker));
        Assert.IsTrue(setup.Errors.Count > 0);
    }

    [TestMethod]
    public async Task RoundCompletion_WaitsForQueuedCommittedEventsAndReplaysDoNotPublishAgain()
    {
        var setup = await Setup();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Events.AfterWrite = async () => { started.TrySetResult(); await release.Task; };
        var presentation = new PointSink();
        using var service = Service(setup, presentation: presentation);
        var firstInput = Death(setup.Players);
        var first = service.RecordDeathAsync(firstInput).AsTask();
        await started.Task;
        var second = service.RecordDeathAsync(Death(setup.Players, Now.AddSeconds(1))).AsTask();
        var complete = service.CompleteRoundAsync("round-1").AsTask();
        release.SetResult();
        await Task.WhenAll(first, second, complete);
        Assert.AreEqual(4, presentation.Changes.Count);
        Assert.AreEqual("summary:round-1", presentation.Order.Last());
        await service.RecordDeathAsync(firstInput);
        Assert.AreEqual(4, presentation.Changes.Count);
        service.Dispose();
        Assert.IsTrue(presentation.Disposed);
    }

    [TestMethod]
    public async Task PlaytimeIntervals_ReuseLedgerRetryVipAndCommittedConsumers()
    {
        var setup = await Setup();
        using var service = Service(setup, vip: 2, playtime: 10);
        setup.Players.TryGet(Attacker, out var player);
        var context = new RankLiveContext(Guid.NewGuid(), Now, false, 4, "round-1");
        await service.TickPlaytimeAsync([player!], context);
        await service.TickPlaytimeAsync([player!], context with { OccurredAtUtc = Now.AddSeconds(5) });
        setup.Events.Fail = true;
        await service.TickPlaytimeAsync([player!], context with { OccurredAtUtc = Now.AddSeconds(10) });
        Assert.IsEmpty(setup.Events.Batches);
        setup.Events.Fail = false;
        await service.TickPlaytimeAsync([player!], context with { OccurredAtUtc = Now.AddSeconds(15) });
        Assert.AreEqual(6L, setup.Events.Points(Attacker));
        Assert.AreEqual("gameplay.PlaytimeInterval", setup.Events.Batches.Values.Single().Source);
        await service.TickPlaytimeAsync([player!], context with { OccurredAtUtc = Now.AddSeconds(15) });
        Assert.HasCount(1, setup.Events.Batches);
        Assert.HasCount(1, setup.Sinks.Changes);
    }

    [TestMethod]
    public async Task TeamObjectives_CommitOneBatchRetryAtomicallyAndReplayBeforeMembershipChanges()
    {
        var setup = await Setup();
        var otherId = new PlayerId(76561198000288103);
        await setup.Players.ConnectAsync(new PlayerConnection(otherId, "Other", PlayerTeam.Terrorist, true, Now));
        setup.Players.TryGet(Attacker, out var player);
        setup.Players.TryGet(otherId, out var other);
        using var service = Service(setup, vip: 2);
        var context = new RankLiveContext(Guid.NewGuid(), Now, false, 4, "round-1");
        setup.Events.Fail = true;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await service.RecordTeamObjectiveAsync(GameplayStatKind.BombExploded, context, [player!, other!]));
        Assert.IsEmpty(setup.Events.Batches);
        setup.Events.Fail = false;
        await service.RecordTeamObjectiveAsync(GameplayStatKind.BombExploded, context, [other!, player!]);
        Assert.HasCount(1, setup.Events.Batches);
        Assert.HasCount(2, setup.Events.Batches.Values.Single().Awards);
        Assert.AreEqual(10L, setup.Events.Points(Attacker));
        Assert.AreEqual(10L, setup.Events.Points(otherId));
        await service.RecordTeamObjectiveAsync(GameplayStatKind.BombExploded, context, [player!]);
        Assert.HasCount(1, setup.Events.Batches);
        Assert.HasCount(2, setup.Sinks.Changes);
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await service.RecordTeamObjectiveAsync(GameplayStatKind.BombExploded, context, [player!, player!]));
    }

    [TestMethod]
    public async Task UnknownRoundAfterReload_DoesNotMintFirstBloodUntilObservedRoundStart()
    {
        var setup = await Setup();
        using var service = Service(setup, firstBlood: 3);
        var death = Death(setup.Players);
        await service.RecordDeathAsync(death with { Context = death.Context with { FirstBloodAvailable = false } });
        var second = Death(setup.Players, Now.AddSeconds(1));
        await service.RecordDeathAsync(second with { Context = second.Context with { FirstBloodAvailable = false } });
        Assert.AreEqual(9L, setup.Events.Points(Attacker));
        var next = Death(setup.Players, Now.AddSeconds(2));
        await service.RecordDeathAsync(next with { Context = next.Context with { RoundKey = "observed-round" } });
        Assert.AreEqual(14L, setup.Events.Points(Attacker));
    }

    private sealed class PointSink : IRankPointEventSink, IDisposable
    {
        public List<RankPointChange> Changes { get; } = [];
        public List<string> Order { get; } = [];
        public bool Disposed { get; private set; }
        public ValueTask CommittedAsync(RankPointChange change, CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            Order.Add("event");
            return ValueTask.CompletedTask;
        }
        public ValueTask CompleteRoundAsync(string roundKey, CancellationToken cancellationToken = default)
        {
            Order.Add("summary:" + roundKey);
            return ValueTask.CompletedTask;
        }
        public void Dispose() => Disposed = true;
    }

    private static LiveRankScoringService Service(SetupResult setup, decimal vip = 1, IRankPointEventSink? presentation = null, int playtime = 0, int firstBlood = 0) => new(
        new RankConfiguration
        {
            Source = RankScoreSource.EventLedger,
            Thresholds = [new("Recruit", 0), new("Promoted", 1)],
            GameplayPoints = new() { [GameplayStatKind.BombPlanted] = 5, [GameplayStatKind.PlaytimeInterval] = 3, [GameplayStatKind.BombExploded] = 5, [GameplayStatKind.FirstBlood] = firstBlood },
            LivePolicy = new() { StreakPoints = new() { [2] = 5 }, VipMultiplier = vip, PlaytimeIntervalSeconds = playtime },
        }, setup.Events, new Scores(setup.Events), setup.Players, setup.Permissions, setup.Sinks, setup.Sinks, setup.Errors.Add, presentation);

    private static RankDeathInput Death(PlayerRegistry players, DateTimeOffset? at = null)
    {
        players.TryGet(Attacker, out var attacker);
        players.TryGet(Victim, out var victim);
        return new(new(Guid.NewGuid(), at ?? Now, false, 4, "round-1"),
            new(victim, PlayerTeam.CounterTerrorist, false), new(attacker, PlayerTeam.Terrorist, false),
            null, "ak47", [], false, 0);
    }

    private static async Task<SetupResult> Setup()
    {
        var players = new PlayerRegistry(new AnoEventBus());
        await players.ConnectAsync(new PlayerConnection(Attacker, "Attacker", PlayerTeam.Terrorist, true, Now));
        await players.ConnectAsync(new PlayerConnection(Victim, "Victim", PlayerTeam.CounterTerrorist, true, Now));
        return new(players, new Events(), new Permissions(), new Sinks(), []);
    }

    private sealed record SetupResult(PlayerRegistry Players, Events Events, Permissions Permissions, Sinks Sinks, List<Exception> Errors);

    private sealed class Events : IRankPointEventRepository
    {
        public Dictionary<Guid, RankPointEventBatch> Batches { get; } = [];
        public bool Fail { get; set; }
        public Func<Task>? AfterWrite { get; set; }
        public long Points(PlayerId id) => Batches.Values.SelectMany(batch => batch.Awards).Where(award => award.PlayerId == id).Sum(award => award.Points);
        public async ValueTask<RankPointEventResult> ApplyAsync(RankPointEventBatch batch, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("test write failure");
            var applied = Batches.TryAdd(batch.EventId, batch);
            if (AfterWrite is not null) await AfterWrite();
            return new(applied, Batches[batch.EventId]);
        }
        public ValueTask<RankPointEventBatch?> ReadAsync(Guid id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Batches.GetValueOrDefault(id));
    }

    private sealed class Permissions : IPermissionEvaluator
    {
        public bool Fail { get; set; }
        public ValueTask<bool> HasPermissionAsync(PlayerId id, PermissionId permission, CancellationToken cancellationToken = default)
            => Fail ? throw new InvalidOperationException("test permission failure") : ValueTask.FromResult(true);
    }

    private sealed class Sinks : IRankTransitionNotificationSink, IRankScoreChangeSink
    {
        public List<PlayerId> Changes { get; } = [];
        public List<PlayerId> Notices { get; } = [];
        public bool Fail { get; set; }
        public ValueTask NotifyAsync(PlayerId id, RankTransition transition, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("test notice failure");
            Notices.Add(id);
            return ValueTask.CompletedTask;
        }
        public ValueTask ScoreChangedAsync(PlayerId id, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("test refresh failure");
            Changes.Add(id);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Scores(Events events) : IGameplayRankScoreRepository
    {
        public ValueTask<CombatScoreRankEntry?> GetScorePlacementAsync(PlayerId id, RankScoreWeights weights, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<CombatScoreRankEntry?>(new(id, Math.Max(0, events.Points(id) + weights.StartingPoints), 1));
        public ValueTask<IReadOnlyList<CombatScoreRankEntry>> GetTopScoresAsync(RankScoreWeights weights, int limit, int offset, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask<long> ReadRawScoreAsync(PlayerId id, RankScoreWeights weights, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask RecordAsync(CombatDeath death, CancellationToken cancellationToken = default) => throw new AssertFailedException("Rank events must not write combat statistics.");
        public ValueTask<CombatTotals> ReadAsync(PlayerId id, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask<IReadOnlyList<CombatRankEntry>> GetTopKillsAsync(int limit, int offset, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopDeathsAsync(int limit, int offset, CancellationToken cancellationToken = default) => throw new AssertFailedException();
        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopAssistsAsync(int limit, int offset, CancellationToken cancellationToken = default) => throw new AssertFailedException();
    }
}
