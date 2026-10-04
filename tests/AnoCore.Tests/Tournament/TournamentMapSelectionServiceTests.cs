using AnoCore.Abstractions.Players;
using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentMapSelectionServiceTests
{
    private static readonly PlayerId A = new(76561198000205101);
    private static readonly PlayerId B = new(76561198000205102);
    private static readonly PlayerId Manager = new(76561198000205199);

    [TestMethod]
    public async Task Complete_PersistsBeforePublishingRuntime()
    {
        var repository = new MemoryRepository();
        var recovery = new TournamentRecoveryService(repository);
        var session = await recovery.BeginAsync(Configuration(), makeActive: true);
        session.Machine.OpenReady();
        session.Machine.Ready(A);
        session.Machine.Ready(B);
        await recovery.SaveAsync(session);

        var runtime = await TournamentMatchRuntime.CreateAsync(recovery);
        var source = new FakeSource();
        using var service = new TournamentMapSelectionService(recovery, runtime, source);

        await service.StartAsync(Manager);
        var current = runtime.CurrentSession!;
        source.Result = new TournamentMapSelectionResult(
            current.Machine.Configuration.MatchId,
            current.Revision,
            ["de_dust2"]);

        var result = await service.CompleteAsync(Manager);

        Assert.AreEqual("de_dust2", result.Maps.Single());
        Assert.AreEqual(TournamentMatchState.Knife, runtime.CurrentSession!.Machine.State);
        Assert.AreEqual("de_dust2", runtime.CurrentSession.Machine.Maps.Single());
        Assert.AreEqual(current.Revision + 1, runtime.CurrentSession.Revision);
    }

    [TestMethod]
    public async Task Complete_RejectsStaleRevisionWithoutChangingRuntime()
    {
        var repository = new MemoryRepository();
        var recovery = new TournamentRecoveryService(repository);
        var session = await recovery.BeginAsync(Configuration(), makeActive: true);
        session.Machine.OpenReady();
        session.Machine.Ready(A);
        session.Machine.Ready(B);
        await recovery.SaveAsync(session);

        var runtime = await TournamentMatchRuntime.CreateAsync(recovery);
        var source = new FakeSource();
        using var service = new TournamentMapSelectionService(recovery, runtime, source);

        await service.StartAsync(Manager);
        var current = runtime.CurrentSession!;
        source.Result = new TournamentMapSelectionResult(
            current.Machine.Configuration.MatchId,
            current.Revision - 1,
            ["de_dust2"]);

        await Assert.ThrowsExactlyAsync<TournamentConcurrencyException>(
            async () => await service.CompleteAsync(Manager));

        Assert.AreEqual(TournamentMatchState.Ready, runtime.CurrentSession!.Machine.State);
        Assert.AreEqual(current.Revision, runtime.CurrentSession.Revision);
    }

    [TestMethod]
    public async Task Complete_PersistenceFailureDoesNotPublishCandidate()
    {
        var repository = new MemoryRepository();
        var recovery = new TournamentRecoveryService(repository);
        var session = await recovery.BeginAsync(Configuration(), makeActive: true);
        session.Machine.OpenReady();
        session.Machine.Ready(A);
        session.Machine.Ready(B);
        await recovery.SaveAsync(session);

        var runtime = await TournamentMatchRuntime.CreateAsync(recovery);
        var source = new FakeSource();
        using var service = new TournamentMapSelectionService(recovery, runtime, source);

        await service.StartAsync(Manager);
        var current = runtime.CurrentSession!;
        source.Result = new TournamentMapSelectionResult(
            current.Machine.Configuration.MatchId,
            current.Revision,
            ["de_dust2"]);
        repository.FailSave = true;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.CompleteAsync(Manager));

        Assert.AreEqual(TournamentMatchState.Ready, runtime.CurrentSession!.Machine.State);
        Assert.AreEqual(current.Revision, runtime.CurrentSession.Revision);
    }

    private static TournamentMatchConfiguration Configuration()
        => new(
            Guid.NewGuid(),
            TournamentBestOf.One,
            new TournamentTeam("Alpha", "A", A, [A]),
            new TournamentTeam("Beta", "B", B, [B]));

    private sealed class FakeSource : ITournamentMapSelectionSource
    {
        public TournamentMapSelectionRequest? Request { get; private set; }
        public TournamentMapSelectionResult? Result { get; set; }
        public int Cancels { get; private set; }

        public ValueTask StartAsync(
            TournamentMapSelectionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.CompletedTask;
        }

        public ValueTask<TournamentMapSelectionResult?> CompleteAsync(
            PlayerId manager,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Result);

        public ValueTask CancelAsync(
            PlayerId manager,
            CancellationToken cancellationToken = default)
        {
            Cancels++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MemoryRepository : ITournamentMatchRepository
    {
        private TournamentStoredMatch? _stored;
        public bool FailSave { get; set; }

        public ValueTask<TournamentStoredMatch> StoreAsync(
            TournamentMatchConfiguration configuration,
            TournamentRecoverySnapshot snapshot,
            bool makeActive,
            CancellationToken cancellationToken = default)
        {
            _stored = new TournamentStoredMatch(configuration, snapshot, 1);
            return ValueTask.FromResult(_stored);
        }

        public ValueTask<TournamentStoredMatch?> LoadAsync(
            Guid matchId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(
                _stored?.Configuration.MatchId == matchId ? _stored : null);

        public ValueTask<TournamentStoredMatch?> LoadActiveAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_stored);

        public ValueTask<TournamentStoredMatch> SaveSnapshotAsync(
            Guid matchId,
            long expectedRevision,
            TournamentRecoverySnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            if (FailSave) throw new InvalidOperationException("forced save failure");
            if (_stored is null) throw new InvalidOperationException("missing match");
            if (_stored.Revision != expectedRevision)
                throw new TournamentConcurrencyException(
                    matchId, expectedRevision, _stored.Revision);
            _stored = new TournamentStoredMatch(
                _stored.Configuration, snapshot, expectedRevision + 1);
            return ValueTask.FromResult(_stored);
        }

        public ValueTask<TournamentStoredMatch> DeactivateAsync(
            Guid matchId,
            long expectedRevision,
            TournamentRecoverySnapshot snapshot,
            CancellationToken cancellationToken = default)
            => SaveSnapshotAsync(matchId, expectedRevision, snapshot, cancellationToken);
    }
}
