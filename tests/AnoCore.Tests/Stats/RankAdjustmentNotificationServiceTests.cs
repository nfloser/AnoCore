using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class RankAdjustmentNotificationServiceTests
{
    private static readonly PlayerId Target = new(76561198000014201);
    private static readonly PlayerId Actor = new(76561198000014202);
    private static readonly DateTimeOffset Now =
        new(2026, 9, 27, 6, 0, 0, TimeSpan.Zero);

    private static RankConfiguration Configuration(bool notify = true) => new()
    {
        KillPoints = 2,
        AssistPoints = 1,
        DeathPenalty = 1,
        NotifyAdministrativeRankChanges = notify,
        Thresholds =
        [
            new RankThreshold("Recruit", 0),
            new RankThreshold("Veteran", 10),
            new RankThreshold("Elite", 100),
        ],
    };

    [TestMethod]
    public async Task ApplyAsync_NotifiesPromotionFromReturnedAdjustments()
    {
        var inner = new StubAdministration(
            new RankAdjustmentAdminResult(1, 2, Guid.NewGuid()));
        var combat = new StubCombat(new CombatTotals(4, 0, 0));
        var sink = new RecordingSink();
        using var service = new RankAdjustmentNotificationService(
            Configuration(), inner, combat, sink);

        var result = await service.ApplyAsync(RankAdjustmentAdminOperation.Give,
            Target, 1, Actor, "reward", Now);

        Assert.AreEqual(2L, result.CurrentPoints);
        Assert.AreEqual(1, combat.Reads);
        var notification = sink.Notifications.Single();
        Assert.AreEqual(Target, notification.PlayerId);
        Assert.AreEqual(RankTransitionKind.Promotion, notification.Transition.Kind);
        Assert.AreEqual(9L, notification.Transition.PreviousPoints);
        Assert.AreEqual(10L, notification.Transition.CurrentPoints);
    }

    [TestMethod]
    public async Task ApplyAsync_CombinesAdjustmentBeforeFlooringNegativeCombatScore()
    {
        var combat = new StubCombat(new CombatTotals(0, 5, 0));
        var sink = new RecordingSink();
        using var service = new RankAdjustmentNotificationService(
            Configuration(),
            new StubAdministration(new RankAdjustmentAdminResult(14, 15, Guid.NewGuid())),
            combat, sink);

        await service.ApplyAsync(RankAdjustmentAdminOperation.Give,
            Target, 1, Actor, "reward", Now);

        var transition = sink.Notifications.Single().Transition;
        Assert.AreEqual(9L, transition.PreviousPoints);
        Assert.AreEqual(10L, transition.CurrentPoints);
        Assert.AreEqual(RankTransitionKind.Promotion, transition.Kind);
    }

    [TestMethod]
    public async Task ApplyAsync_NotifiesDemotionAndSkipsWithinRankChange()
    {
        var combat = new StubCombat(new CombatTotals(4, 0, 0));
        var sink = new RecordingSink();
        using (var service = new RankAdjustmentNotificationService(
            Configuration(),
            new StubAdministration(new RankAdjustmentAdminResult(2, 1, Guid.NewGuid())),
            combat, sink))
        {
            await service.ApplyAsync(RankAdjustmentAdminOperation.Take,
                Target, 1, Actor, "penalty", Now);
        }

        Assert.AreEqual(RankTransitionKind.Demotion,
            sink.Notifications.Single().Transition.Kind);
        sink.Notifications.Clear();
        using var unchanged = new RankAdjustmentNotificationService(
            Configuration(),
            new StubAdministration(new RankAdjustmentAdminResult(3, 4, Guid.NewGuid())),
            combat, sink);
        await unchanged.ApplyAsync(RankAdjustmentAdminOperation.Give,
            Target, 1, Actor, "reward", Now);
        Assert.AreEqual(0, sink.Notifications.Count);
    }

    [TestMethod]
    public async Task DisabledNotification_ReturnsMutationWithoutCombatRead()
    {
        var expected = new RankAdjustmentAdminResult(1, 2, Guid.NewGuid());
        var inner = new StubAdministration(expected);
        var combat = new StubCombat(new CombatTotals(4, 0, 0));
        using var service = new RankAdjustmentNotificationService(
            Configuration(notify: false), inner, combat, new RecordingSink());

        var actual = await service.ApplyAsync(RankAdjustmentAdminOperation.Give,
            Target, 1, Actor, "reward", Now);

        Assert.AreSame(expected, actual);
        Assert.AreEqual(1, inner.Calls);
        Assert.AreEqual(0, combat.Reads);
    }

    [TestMethod]
    public async Task NotificationFailure_DoesNotTurnCommittedMutationIntoFailure()
    {
        var expected = new RankAdjustmentAdminResult(1, 2, Guid.NewGuid());
        var errors = new List<Exception>();
        using var service = new RankAdjustmentNotificationService(
            Configuration(), new StubAdministration(expected),
            new StubCombat(new CombatTotals(4, 0, 0)),
            new ThrowingSink(), errors.Add);

        var actual = await service.ApplyAsync(RankAdjustmentAdminOperation.Give,
            Target, 1, Actor, "reward", Now);

        Assert.AreSame(expected, actual);
        Assert.AreEqual(1, errors.Count);
        Assert.IsInstanceOfType<InvalidOperationException>(errors[0]);
    }

    [TestMethod]
    public async Task MutationFailure_PropagatesWithoutNotificationWork()
    {
        var inner = new StubAdministration(
            new RankAdjustmentAdminResult(0, 0, Guid.NewGuid()))
        {
            Failure = new InvalidOperationException("mutation failed"),
        };
        var combat = new StubCombat(new CombatTotals(4, 0, 0));
        var sink = new RecordingSink();
        using var service = new RankAdjustmentNotificationService(
            Configuration(), inner, combat, sink);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await service.ApplyAsync(RankAdjustmentAdminOperation.Set,
                Target, 1, Actor, "reason", Now).AsTask());
        Assert.AreEqual(0, combat.Reads);
        Assert.AreEqual(0, sink.Notifications.Count);
    }

    [TestMethod]
    public async Task Dispose_DisposesSinkAndRejectsNewMutations()
    {
        var sink = new RecordingSink();
        var service = new RankAdjustmentNotificationService(
            Configuration(),
            new StubAdministration(new RankAdjustmentAdminResult(0, 0, Guid.NewGuid())),
            new StubCombat(new CombatTotals(0, 0, 0)), sink);
        service.Dispose();
        service.Dispose();

        Assert.AreEqual(1, sink.Disposals);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
            await service.ApplyAsync(RankAdjustmentAdminOperation.Reset,
                Target, 0, Actor, "reset", Now).AsTask());
    }

    private sealed class StubAdministration(RankAdjustmentAdminResult result)
        : IRankAdjustmentAdministrationService
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }

        public ValueTask<RankAdjustmentAdminResult> ApplyAsync(
            RankAdjustmentAdminOperation operation, PlayerId targetId, long points,
            PlayerId? actorId, string reason, DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Failure is not null) throw Failure;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StubCombat(CombatTotals totals) : ICombatRepository
    {
        public int Reads { get; private set; }

        public ValueTask<CombatTotals> ReadAsync(PlayerId playerId,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(totals);
        }

        public ValueTask RecordAsync(CombatDeath death,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<CombatRankEntry>> GetTopKillsAsync(
            int limit, int offset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatRankEntry>>([]);

        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopDeathsAsync(
            int limit, int offset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatCountRankEntry>>([]);

        public ValueTask<IReadOnlyList<CombatCountRankEntry>> GetTopAssistsAsync(
            int limit, int offset, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<CombatCountRankEntry>>([]);
    }

    private sealed class RecordingSink : IRankTransitionNotificationSink, IDisposable
    {
        public List<(PlayerId PlayerId, RankTransition Transition)> Notifications { get; } = [];
        public int Disposals { get; private set; }

        public ValueTask NotifyAsync(PlayerId playerId, RankTransition transition,
            CancellationToken cancellationToken = default)
        {
            Notifications.Add((playerId, transition));
            return ValueTask.CompletedTask;
        }

        public void Dispose() => Disposals++;
    }

    private sealed class ThrowingSink : IRankTransitionNotificationSink
    {
        public ValueTask NotifyAsync(PlayerId playerId, RankTransition transition,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("delivery failed"));
    }
}
