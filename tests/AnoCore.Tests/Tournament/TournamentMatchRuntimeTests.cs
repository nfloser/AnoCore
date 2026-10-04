using AnoCore.Abstractions.Players;
using AnoCore.Modules.Tournament;

namespace AnoCore.Tests.Tournament;

[TestClass]
public sealed class TournamentMatchRuntimeTests
{
    private static readonly PlayerId A1 = new(76561198000195101);
    private static readonly PlayerId B1 = new(76561198000195111);

    [TestMethod]
    public async Task CreateAsync_RestoresActiveAssignments()
    {
        var configuration = Configuration();
        var machine = new TournamentMatchStateMachine(configuration);
        var repository = new FakeRepository(
            new TournamentStoredMatch(configuration, machine.Snapshot(), 4));
        var runtime = await TournamentMatchRuntime.CreateAsync(
            new TournamentRecoveryService(repository));

        Assert.AreEqual(configuration.MatchId, runtime.ActiveMatchId);
        Assert.IsTrue(runtime.TryGetAssignedSide(A1, out var aSide));
        Assert.AreEqual(PlayerTeam.Terrorist, aSide);
        Assert.IsTrue(runtime.TryGetAssignedSide(B1, out var bSide));
        Assert.AreEqual(PlayerTeam.CounterTerrorist, bSide);
    }

    [TestMethod]
    public async Task CreateAsync_WithNoActiveMatch_IsEmpty()
    {
        var runtime = await TournamentMatchRuntime.CreateAsync(
            new TournamentRecoveryService(new FakeRepository(null)));

        Assert.IsNull(runtime.ActiveMatchId);
        Assert.IsFalse(runtime.TryGetAssignedSide(A1, out var side));
        Assert.AreEqual(PlayerTeam.Unknown, side);
    }

    private static TournamentMatchConfiguration Configuration()
        => new(
            Guid.NewGuid(),
            TournamentBestOf.One,
            new TournamentTeam("Alpha", "A", A1, [A1]),
            new TournamentTeam("Beta", "B", B1, [B1]));

    private sealed class FakeRepository(TournamentStoredMatch? active)
        : ITournamentMatchRepository
    {
        public ValueTask<TournamentStoredMatch?> LoadActiveAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(active);

        public ValueTask<TournamentStoredMatch?> LoadAsync(
            Guid matchId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<TournamentStoredMatch?>(null);

        public ValueTask<TournamentStoredMatch> StoreAsync(
            TournamentMatchConfiguration configuration,
            TournamentRecoverySnapshot snapshot,
            bool makeActive,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

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
}
