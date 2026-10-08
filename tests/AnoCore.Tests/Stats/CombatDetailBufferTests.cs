using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;
using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class CombatDetailBufferTests
{
    [TestMethod]
    public async Task TenPlayerLoadUsesBoundedBatchesRatherThanOneTransactionPerEvent()
    {
        var repository = new Sink();
        var buffer = Create(repository);
        for (var tick = 0; tick < 20; tick++)
        {
            for (var player = 0; player < 10; player++)
                for (var shot = 0; shot < 50; shot++)
                    Assert.IsTrue(buffer.TryEnqueue(CombatDetailBatchTests.Fire()));
            await buffer.FlushAsync();
        }
        Assert.AreEqual(10000, repository.Batches.Sum(batch => batch.Count));
        Assert.AreEqual(40, repository.Batches.Count);
        Assert.IsTrue(repository.Batches.All(batch => batch.Count <= 256));
        Assert.AreEqual(0, buffer.PendingCount);
        await buffer.StopAsync();
    }

    [TestMethod]
    public async Task FailedWriteAndCancellationKeepAcceptedEventsForRetry()
    {
        var sink = new Sink { Failure = new InvalidOperationException("database unavailable") };
        var buffer = Create(sink);
        var fire = CombatDetailBatchTests.Fire();
        Assert.IsTrue(buffer.TryEnqueue(fire));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await buffer.FlushAsync());
        Assert.AreEqual(1, buffer.PendingCount);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await buffer.FlushAsync(cancelled.Token));
        Assert.AreEqual(1, buffer.PendingCount);
        sink.Failure = null;
        await buffer.FlushAsync();
        Assert.AreEqual(fire.EventId, sink.Batches.Single().WeaponFire.Single().EventId);
        Assert.AreEqual(0, buffer.PendingCount);
        await buffer.StopAsync();
    }

    [TestMethod]
    public async Task CapacityIncludesInFlightBatchAndRejectsWithoutDroppingAcceptedEvents()
    {
        var sink = new Sink { Started = new(), Release = new() };
        var buffer = Create(sink, capacity: 256);
        for (var i = 0; i < 256; i++) Assert.IsTrue(buffer.TryEnqueue(CombatDetailBatchTests.Fire()));
        var flush = buffer.FlushAsync().AsTask();
        await sink.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(buffer.TryEnqueue(CombatDetailBatchTests.Fire()));
        Assert.AreEqual(256, buffer.PendingCount);
        Assert.AreEqual(1L, buffer.RejectedCount);
        sink.Release.SetResult();
        await flush;
        Assert.AreEqual(256, sink.Batches.Single().Count);
        Assert.IsTrue(buffer.TryEnqueue(CombatDetailBatchTests.Fire()));
        await buffer.StopAsync();
    }

    [TestMethod]
    public async Task ConcurrentFlushesDoNotWriteTheSameBatchTwiceAndNewEventsSurvive()
    {
        var sink = new Sink { Started = new(), Release = new() };
        var buffer = Create(sink);
        buffer.TryEnqueue(CombatDetailBatchTests.Fire());
        var first = buffer.FlushAsync().AsTask();
        await sink.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        buffer.TryEnqueue(CombatDetailBatchTests.Fire());
        var second = buffer.FlushAsync().AsTask();
        sink.Release.SetResult();
        await Task.WhenAll(first, second);
        Assert.AreEqual(2, sink.Batches.Sum(batch => batch.Count));
        Assert.AreEqual(2, sink.Batches.Count);
        await buffer.StopAsync();
    }

    [TestMethod]
    public async Task StopRejectsNewEventsAndFlushesMixedMapAndDamagePayloads()
    {
        var sink = new Sink();
        var buffer = Create(sink);
        var fire = CombatDetailBatchTests.Fire();
        var damage = new CombatDamageEvent(Guid.NewGuid(), new(76561198000012201),
            fire.PlayerId, fire.OccurredAtUtc, "workshop/custom", "hegrenade", 2, 42, 8);
        buffer.TryEnqueue(fire);
        buffer.TryEnqueue(damage);
        await buffer.StopAsync();
        await buffer.StopAsync();
        Assert.IsFalse(buffer.TryEnqueue(fire));
        Assert.AreEqual("workshop/custom", sink.Batches.Single().Damage.Single().MapName);
        Assert.AreEqual(42, sink.Batches.Single().Damage.Single().DamageHealth);
        Assert.AreEqual(2, sink.Batches.Single().Count);
    }

    [TestMethod]
    public async Task FailedShutdownReportsRemainingDataAndCanBeRetried()
    {
        var sink = new Sink { Failure = new InvalidOperationException("offline") };
        var buffer = Create(sink);
        buffer.TryEnqueue(CombatDetailBatchTests.Fire());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await buffer.StopAsync());
        Assert.AreEqual(1, buffer.PendingCount);
        Assert.IsFalse(buffer.TryEnqueue(CombatDetailBatchTests.Fire()));
        sink.Failure = null;
        await buffer.StopAsync();
        Assert.AreEqual(0, buffer.PendingCount);
    }

    private static CombatDetailBuffer Create(Sink sink, int capacity = 8192)
        => new(sink, new CombatRecordingConfiguration { Capacity = capacity });

    [TestMethod]
    public async Task PeriodicWorkerRecoversAfterFailureWithoutLosingQueuedEvent()
    {
        var sink = new Sink { Failure = new InvalidOperationException("offline"), Committed = new() };
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var buffer = new CombatDetailBuffer(sink, new() { FlushIntervalSeconds = 0.1 });
        buffer.TryEnqueue(CombatDetailBatchTests.Fire());
        buffer.Start(_ => reported.TrySetResult());
        try
        {
            await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, buffer.PendingCount);
            sink.Failure = null;
            await sink.Committed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await buffer.StopAsync(); }
        Assert.AreEqual(1, sink.Batches.Sum(batch => batch.Count));
        Assert.AreEqual(0, buffer.PendingCount);
    }

    [TestMethod]
    public async Task ShutdownTimeoutCancelsBlockedWriteAndRetainsPendingEvent()
    {
        var sink = new Sink { Started = new(), Release = new() };
        var buffer = new CombatDetailBuffer(sink, new() { ShutdownTimeoutSeconds = 1 });
        buffer.TryEnqueue(CombatDetailBatchTests.Fire());
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await buffer.StopAsync());
        Assert.AreEqual(1, buffer.PendingCount);
        sink.Release.SetResult();
        await buffer.StopAsync();
        Assert.AreEqual(0, buffer.PendingCount);
    }

    private sealed class Sink : ICombatDetailBatchRepository
    {
        public List<CombatDetailBatch> Batches { get; } = [];
        public Exception? Failure { get; set; }
        public TaskCompletionSource? Started { get; set; }
        public TaskCompletionSource? Release { get; set; }
        public TaskCompletionSource? Committed { get; set; }

        public async ValueTask RecordDetailsAsync(CombatDetailBatch batch,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Started?.TrySetResult();
            if (Release is not null) await Release.Task.WaitAsync(cancellationToken);
            if (Failure is not null) throw Failure;
            Batches.Add(batch);
            Committed?.TrySetResult();
        }
    }
}
