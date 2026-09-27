using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class RankTransitionMonitorTests
{
    private static readonly PlayerId Victim = new(76561198000014101);
    private static readonly PlayerId Attacker = new(76561198000014102);
    private static readonly PlayerId Assister = new(76561198000014103);
    private static readonly DateTimeOffset Now =
        new(2026, 9, 27, 2, 0, 0, TimeSpan.Zero);

    private static RankConfiguration Configuration(bool notify = true) => new()
    {
        KillPoints = 2,
        AssistPoints = 1,
        DeathPenalty = 1,
        NotifyRankChanges = notify,
        Thresholds =
        [
            new RankThreshold("Recruit", 0),
            new RankThreshold("Veteran", 10),
            new RankThreshold("Elite", 100),
        ],
    };

    [TestMethod]
    public async Task RecordAsync_NotifiesAllAffectedThresholdCrossingsAfterWrite()
    {
        var repository = new FakeRepository
        {
            Points =
            {
                [Victim] = 10,
                [Attacker] = 9,
                [Assister] = 9,
            },
        };
        var sink = new RecordingSink(repository);
        using var monitor = new RankTransitionMonitor(
            Configuration(), repository, sink);
        var death = new CombatDeath(
            Guid.NewGuid(), Victim, Attacker, Assister, Now);

        await monitor.RecordAsync(death);

        Assert.AreEqual(3, sink.Notifications.Count);
        Assert.IsTrue(sink.AllObservedAfterWrite);
        Assert.AreEqual(RankTransitionKind.Demotion,
            sink.Notifications.Single(x => x.PlayerId == Victim).Transition.Kind);
        Assert.AreEqual(RankTransitionKind.Promotion,
            sink.Notifications.Single(x => x.PlayerId == Attacker).Transition.Kind);
        Assert.AreEqual(RankTransitionKind.Promotion,
            sink.Notifications.Single(x => x.PlayerId == Assister).Transition.Kind);
    }

    [TestMethod]
    public async Task RecordAsync_ReplayAndMovementInsideRankDoNotNotify()
    {
        var repository = new FakeRepository
        {
            Points =
            {
                [Victim] = 20,
                [Attacker] = 20,
            },
        };
        var sink = new RecordingSink(repository);
        using var monitor = new RankTransitionMonitor(
            Configuration(), repository, sink);
        var death = new CombatDeath(
            Guid.NewGuid(), Victim, Attacker, null, Now);

        await monitor.RecordAsync(death);
        await monitor.RecordAsync(death);

        Assert.AreEqual(0, sink.Notifications.Count);
        Assert.AreEqual(1, repository.Writes);
    }

    [TestMethod]
    public async Task DisabledNotifications_WriteWithoutScoreSnapshots()
    {
        var repository = new FakeRepository();
        var sink = new RecordingSink(repository);
        using var monitor = new RankTransitionMonitor(
            Configuration(notify: false), repository, sink);

        await monitor.RecordAsync(new CombatDeath(
            Guid.NewGuid(), Victim, Attacker, null, Now));

        Assert.AreEqual(1, repository.Writes);
        Assert.AreEqual(0, repository.ScoreReads);
        Assert.AreEqual(0, sink.Notifications.Count);
    }

    [TestMethod]
    public async Task RecordAsync_PropagatesCancellationBeforeWrite()
    {
        var repository = new FakeRepository();
        using var monitor = new RankTransitionMonitor(
            Configuration(), repository, new RecordingSink(repository));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await monitor.RecordAsync(new CombatDeath(
                Guid.NewGuid(), Victim, Attacker, null, Now),
                cancellation.Token).AsTask());
        Assert.AreEqual(0, repository.Writes);
    }

    [TestMethod]
    public async Task Dispose_RejectsNewWrites()
    {
        var repository = new FakeRepository();
        var monitor = new RankTransitionMonitor(
            Configuration(), repository, new RecordingSink(repository));
        monitor.Dispose();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            async () => await monitor.RecordAsync(new CombatDeath(
                Guid.NewGuid(), Victim, Attacker, null, Now)).AsTask());
        Assert.AreEqual(0, repository.Writes);
    }

    private sealed class RecordingSink(FakeRepository repository)
        : IRankTransitionNotificationSink
    {
        public List<(PlayerId PlayerId, RankTransition Transition)> Notifications { get; } = [];
        public bool AllObservedAfterWrite { get; private set; } = true;

        public ValueTask NotifyAsync(PlayerId playerId, RankTransition transition,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AllObservedAfterWrite &= repository.Writes > 0;
            Notifications.Add((playerId, transition));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeRepository : ICombatRepository
    {
        private readonly HashSet<Guid> _events = [];
        public Dictionary<PlayerId, long> Points { get; } = [];
        public int Writes { get; private set; }
        public int ScoreReads { get; private set; }

        public ValueTask RecordAsync(CombatDeath death,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_events.Add(death.EventId)) return ValueTask.CompletedTask;
            Writes++;
            Points[death.VictimId] = Math.Max(0, Get(death.VictimId) - 1);
            if (death.AttackerId is not null && !death.IsTeamKill)
                Points[death.AttackerId] = Get(death.AttackerId) + 2;
            if (death.AssisterId is not null)
                Points[death.AssisterId] = Get(death.AssisterId) + 1;
            return ValueTask.CompletedTask;
        }

        public ValueTask<CombatScoreRankEntry?> GetScorePlacementAsync(
            PlayerId playerId, int killPoints, int assistPoints, int deathPenalty,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScoreReads++;
            return ValueTask.FromResult<CombatScoreRankEntry?>(
                Points.TryGetValue(playerId, out var points)
                    ? new CombatScoreRankEntry(playerId, points, 1)
                    : null);
        }

        public ValueTask<CombatTotals> ReadAsync(PlayerId playerId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new CombatTotals(0, 0, 0));

        public ValueTask<IReadOnlyList<CombatRankEntry>> GetTopKillsAsync(
            int limit, int offset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatRankEntry>>([]);

        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopDeathsAsync(
            int limit, int offset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatCountRankEntry>>([]);

        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopAssistsAsync(
            int limit, int offset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatCountRankEntry>>([]);

        private long Get(PlayerId id) => Points.GetValueOrDefault(id);
    }
}
