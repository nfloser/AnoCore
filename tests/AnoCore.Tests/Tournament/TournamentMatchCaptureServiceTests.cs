using AnoCore.Abstractions.Players;
using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentMatchCaptureServiceTests
{
    private static readonly PlayerId A = new(76561198000198001);
    private static readonly PlayerId B = new(76561198000198002);

    [TestMethod]
    public async Task StartAndCapture_AreIdempotentForSameMapAndRevision()
    {
        var fixture = await Fixture.CreateAsync();
        await using var service = new TournamentMatchCaptureService(
            fixture.Runtime, fixture.Transport);

        var firstDemo = await service.StartDemoAsync(7);
        var secondDemo = await service.StartDemoAsync(7);
        var firstBackup = await service.CaptureRoundAsync(4, 7);
        var secondBackup = await service.CaptureRoundAsync(4, 7);

        Assert.AreEqual(firstDemo, secondDemo);
        Assert.AreEqual(firstBackup, secondBackup);
        Assert.AreEqual(1, fixture.Transport.Started.Count);
        Assert.AreEqual(1, fixture.Transport.Captured.Count);
        Assert.AreEqual(1, service.Backups.Count);
    }

    [TestMethod]
    public async Task StaleRevision_FailsBeforeTransportWork()
    {
        var fixture = await Fixture.CreateAsync();
        await using var service = new TournamentMatchCaptureService(
            fixture.Runtime, fixture.Transport);

        await Assert.ThrowsExactlyAsync<TournamentConcurrencyException>(async () =>
            await service.StartDemoAsync(6));
        await Assert.ThrowsExactlyAsync<TournamentConcurrencyException>(async () =>
            await service.CaptureRoundAsync(1, 8));

        Assert.AreEqual(0, fixture.Transport.Started.Count);
        Assert.AreEqual(0, fixture.Transport.Captured.Count);
    }

    [TestMethod]
    public async Task Restore_RejectsBackupAfterCurrentMapChanges()
    {
        var fixture = await Fixture.CreateAsync();
        await using var service = new TournamentMatchCaptureService(
            fixture.Runtime, fixture.Transport);
        var backup = await service.CaptureRoundAsync(10, 7);

        fixture.AdvanceToSecondMap(revision: 8);
        var restored = await fixture.Recovery.RestoreActiveAsync();
        Assert.IsNotNull(restored);
        fixture.Runtime.Replace(restored);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await service.RestoreAsync(backup.Id, 8));

        Assert.AreEqual(0, fixture.Transport.Restored.Count);
    }

    [TestMethod]
    public async Task ConcurrentCapture_SerializesDuplicateTransportCalls()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Transport.CaptureEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.ReleaseCapture = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = new TournamentMatchCaptureService(
            fixture.Runtime, fixture.Transport);

        var first = service.CaptureRoundAsync(7, 7).AsTask();
        await fixture.Transport.CaptureEntered.Task;
        var second = service.CaptureRoundAsync(7, 7).AsTask();
        fixture.Transport.ReleaseCapture.TrySetResult(true);

        await Task.WhenAll(first, second);

        Assert.AreEqual(1, fixture.Transport.Captured.Count);
        Assert.AreEqual(first.Result, second.Result);
    }

    [TestMethod]
    public async Task StartingNextMapDemo_StopsOwnedPreviousDemoFirst()
    {
        var fixture = await Fixture.CreateAsync();
        await using var service = new TournamentMatchCaptureService(
            fixture.Runtime, fixture.Transport);
        var first = await service.StartDemoAsync(7);

        fixture.AdvanceToSecondMap(revision: 8);
        fixture.Runtime.Replace((await fixture.Recovery.RestoreActiveAsync())!);
        var second = await service.StartDemoAsync(8);

        Assert.AreEqual(first, fixture.Transport.Stopped.Single());
        Assert.AreNotEqual(first.Id, second.Id);
        Assert.AreEqual(2, fixture.Transport.Started.Count);
    }

    [TestMethod]
    public async Task Restore_UsesKnownCurrentMapBackupAndExpectedRevision()
    {
        var fixture = await Fixture.CreateAsync();
        await using var service = new TournamentMatchCaptureService(
            fixture.Runtime, fixture.Transport);
        var backup = await service.CaptureRoundAsync(12, 7);

        var restored = await service.RestoreAsync(backup.Id, 7);

        Assert.AreEqual(backup, restored);
        Assert.AreEqual(backup, fixture.Transport.Restored.Single());
    }

    [TestMethod]
    public async Task DisposeDuringStart_WaitsThenStopsTheOwnedDemo()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Transport.StartEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.ReleaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new TournamentMatchCaptureService(
            fixture.Runtime, fixture.Transport);

        var start = service.StartDemoAsync(7).AsTask();
        await fixture.Transport.StartEntered.Task;
        var dispose = service.DisposeAsync().AsTask();
        fixture.Transport.ReleaseStart.TrySetResult(true);

        await Task.WhenAll(start, dispose);

        Assert.AreEqual(1, fixture.Transport.Started.Count);
        Assert.AreEqual(1, fixture.Transport.Stopped.Count);
        Assert.AreEqual(start.Result, fixture.Transport.Stopped.Single());
        Assert.IsNull(service.ActiveDemo);
    }

    [TestMethod]
    public async Task QueuedStart_AfterDisposeBegins_DoesNotStartDemo()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Transport.CaptureEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.ReleaseCapture = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new TournamentMatchCaptureService(
            fixture.Runtime, fixture.Transport);

        var capture = service.CaptureRoundAsync(1, 7).AsTask();
        await fixture.Transport.CaptureEntered.Task;
        var start = service.StartDemoAsync(7).AsTask();
        var dispose = service.DisposeAsync().AsTask();
        fixture.Transport.ReleaseCapture.TrySetResult(true);

        await capture;
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () => await start);
        await dispose;

        Assert.AreEqual(0, fixture.Transport.Started.Count);
        Assert.IsNull(service.ActiveDemo);
    }

    [TestMethod]
    public async Task AsyncDispose_StopsDemoAndIsolatesCleanupFailure()
    {
        var fixture = await Fixture.CreateAsync();
        Exception? observed = null;
        var service = new TournamentMatchCaptureService(
            fixture.Runtime, fixture.Transport, cleanupError: exception => observed = exception);
        await service.StartDemoAsync(7);
        fixture.Transport.StopException = new InvalidOperationException("stop failed");

        await service.DisposeAsync();

        Assert.IsNotNull(observed);
        Assert.AreEqual("stop failed", observed.Message);
        Assert.IsNull(service.ActiveDemo);
    }

    [TestMethod]
    public async Task BackupRetention_IsBoundedAndNewestFirst()
    {
        var fixture = await Fixture.CreateAsync();
        await using var service = new TournamentMatchCaptureService(
            fixture.Runtime, fixture.Transport, maxRetainedBackups: 2);

        var one = await service.CaptureRoundAsync(1, 7);
        var two = await service.CaptureRoundAsync(2, 7);
        var three = await service.CaptureRoundAsync(3, 7);

        CollectionAssert.AreEqual(
            new[] { three.Id, two.Id },
            service.Backups.Select(value => value.Id).ToArray());
        Assert.IsFalse(service.Backups.Any(value => value.Id == one.Id));
    }

    private sealed class Fixture
    {
        private Fixture(
            FakeRepository repository,
            TournamentRecoveryService recovery,
            TournamentMatchRuntime runtime,
            FakeTransport transport)
        {
            Repository = repository;
            Recovery = recovery;
            Runtime = runtime;
            Transport = transport;
        }

        public FakeRepository Repository { get; }
        public TournamentRecoveryService Recovery { get; }
        public TournamentMatchRuntime Runtime { get; }
        public FakeTransport Transport { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var configuration = Configuration();
            var machine = LiveMachine(configuration);
            var repository = new FakeRepository
            {
                Active = new TournamentStoredMatch(configuration, machine.Snapshot(), 7),
            };
            var recovery = new TournamentRecoveryService(repository);
            var runtime = await TournamentMatchRuntime.CreateAsync(recovery);
            return new Fixture(repository, recovery, runtime, new FakeTransport());
        }

        public void AdvanceToSecondMap(long revision)
        {
            var stored = Repository.Active!;
            var machine = TournamentMatchStateMachine.Restore(
                stored.Configuration, stored.Snapshot);
            machine.CompleteMap(TournamentTeamSlot.TeamA);
            machine.CompleteKnife(TournamentTeamSlot.TeamA);
            machine.ChooseSide(TournamentTeamSlot.TeamA, PlayerTeam.Terrorist);
            Repository.Active = new TournamentStoredMatch(
                stored.Configuration, machine.Snapshot(), revision);
        }

        private static TournamentMatchConfiguration Configuration()
            => new(
                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                TournamentBestOf.Three,
                new TournamentTeam("Alpha", "A", A, [A]),
                new TournamentTeam("Beta", "B", B, [B]));

        private static TournamentMatchStateMachine LiveMachine(
            TournamentMatchConfiguration configuration)
        {
            var machine = new TournamentMatchStateMachine(configuration);
            machine.OpenReady();
            machine.Ready(A);
            machine.Ready(B);
            machine.BeginVeto();
            machine.CompleteVeto(["de_dust2", "de_nuke", "de_mirage"]);
            machine.CompleteKnife(TournamentTeamSlot.TeamA);
            machine.ChooseSide(TournamentTeamSlot.TeamA, PlayerTeam.Terrorist);
            return machine;
        }
    }

    private sealed class FakeRepository : ITournamentMatchRepository
    {
        public TournamentStoredMatch? Active { get; set; }

        public ValueTask<TournamentStoredMatch> StoreAsync(
            TournamentMatchConfiguration configuration,
            TournamentRecoverySnapshot snapshot,
            bool makeActive,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<TournamentStoredMatch?> LoadAsync(
            Guid matchId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(
                Active?.Configuration.MatchId == matchId ? Active : null);

        public ValueTask<TournamentStoredMatch?> LoadActiveAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Active);

        public ValueTask<TournamentStoredMatch> SaveSnapshotAsync(
            Guid matchId,
            long expectedRevision,
            TournamentRecoverySnapshot snapshot,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<TournamentStoredMatch> DeactivateAsync(
            Guid matchId,
            long expectedRevision,
            TournamentRecoverySnapshot snapshot,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeTransport : ITournamentMatchCaptureTransport
    {
        public List<TournamentDemoSession> Started { get; } = [];
        public List<TournamentDemoSession> Stopped { get; } = [];
        public List<TournamentRoundBackup> Captured { get; } = [];
        public List<TournamentRoundBackup> Restored { get; } = [];
        public TaskCompletionSource<bool>? StartEntered { get; set; }
        public TaskCompletionSource<bool>? ReleaseStart { get; set; }
        public TaskCompletionSource<bool>? CaptureEntered { get; set; }
        public TaskCompletionSource<bool>? ReleaseCapture { get; set; }
        public Exception? StopException { get; set; }

        public async ValueTask StartDemoAsync(
            TournamentDemoSession demo,
            CancellationToken cancellationToken = default)
        {
            Started.Add(demo);
            StartEntered?.TrySetResult(true);
            if (ReleaseStart is not null)
                await ReleaseStart.Task.WaitAsync(cancellationToken);
        }

        public ValueTask StopDemoAsync(
            TournamentDemoSession demo,
            CancellationToken cancellationToken = default)
        {
            Stopped.Add(demo);
            return StopException is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(StopException);
        }

        public async ValueTask CaptureBackupAsync(
            TournamentRoundBackup backup,
            CancellationToken cancellationToken = default)
        {
            Captured.Add(backup);
            CaptureEntered?.TrySetResult(true);
            if (ReleaseCapture is not null)
                await ReleaseCapture.Task.WaitAsync(cancellationToken);
        }

        public ValueTask RestoreBackupAsync(
            TournamentRoundBackup backup,
            CancellationToken cancellationToken = default)
        {
            Restored.Add(backup);
            return ValueTask.CompletedTask;
        }
    }
}
