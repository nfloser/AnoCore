using AnoCore.Abstractions.Players;
using AnoCore.Modules.Progression;

namespace AnoCore.Tests.Progression;

[TestClass]
public sealed class ProgressionGrantServiceTests
{
    private static readonly PlayerId Player = new(76561198000238001);
    private static readonly DateTimeOffset Friday =
        new(2026, 10, 9, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task GameplayGrant_AppliesBoostAndReturnsDerivedLevel()
    {
        var repository = new MemoryGrantRepository();
        var service = new ProgressionGrantService(repository, Definitions());

        var result = await service.GrantAsync(Player,
            new ProgressionGrantRequest("kill:round-1", ProgressionXpSource.Gameplay,
                60, "gameplay.kill", Friday));

        Assert.IsTrue(result.Applied);
        Assert.AreEqual(120L, result.Grant.AwardedXp);
        Assert.AreEqual("double-weekend", result.Grant.BoostId);
        Assert.AreEqual(2m, result.Grant.BoostMultiplier);
        Assert.AreEqual(120L, result.Grant.LifetimeXpAfter);
        Assert.AreEqual(2, result.LevelAfter.Level);
    }

    [TestMethod]
    public async Task Retry_UsesPersistedGrantBeforeReevaluatingChangedDefinitions()
    {
        var existing = new ProgressionGrantRecord(
            Player, "event-1", ProgressionXpSource.Gameplay, 100, 200,
            "gameplay.round", Friday, "old-double", 2m, 200, 1);
        var repository = new MemoryGrantRepository(existing);
        var changedDefinitions = ProgressionDefinitionSnapshot.Create(
            [new(1, 0), new(2, 100), new(3, 200)],
            [new("new-triple", Friday, Friday.AddHours(1), 3m)]);
        var service = new ProgressionGrantService(repository, changedDefinitions);

        var result = await service.GrantAsync(Player,
            new ProgressionGrantRequest("event-1", ProgressionXpSource.Gameplay,
                100, "gameplay.round", Friday.ToOffset(TimeSpan.FromHours(2))));

        Assert.IsFalse(result.Applied);
        Assert.AreEqual(200L, result.Grant.AwardedXp);
        Assert.AreEqual("old-double", result.Grant.BoostId);
        Assert.AreEqual(3, result.LevelAfter.Level);
        Assert.AreEqual(0, repository.ApplyCalls);
    }

    [TestMethod]
    public async Task Retry_WithDifferentOriginalPayloadIsRejected()
    {
        var existing = new ProgressionGrantRecord(
            Player, "event-1", ProgressionXpSource.Gameplay, 100, 200,
            "gameplay.round", Friday, "old-double", 2m, 200, 1);
        var service = new ProgressionGrantService(
            new MemoryGrantRepository(existing), Definitions());

        await Assert.ThrowsExactlyAsync<ProgressionGrantConflictException>(async () =>
            await service.GrantAsync(Player,
                new ProgressionGrantRequest("event-1", ProgressionXpSource.Gameplay,
                    101, "gameplay.round", Friday)));
    }

    [TestMethod]
    public async Task InvalidRequests_AreRejectedBeforeRepositoryMutation()
    {
        var repository = new MemoryGrantRepository();
        var service = new ProgressionGrantService(repository, Definitions());

        foreach (var request in new[]
        {
            new ProgressionGrantRequest("", ProgressionXpSource.Gameplay, 1, "reason", Friday),
            new ProgressionGrantRequest(" id ", ProgressionXpSource.Gameplay, 1, "reason", Friday),
            new ProgressionGrantRequest("id", ProgressionXpSource.Gameplay, -1, "reason", Friday),
            new ProgressionGrantRequest("id", ProgressionXpSource.Gameplay, 1, "", Friday),
            new ProgressionGrantRequest("id", (ProgressionXpSource)99, 1, "reason", Friday),
        })
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
                await service.GrantAsync(Player, request));
        }

        Assert.AreEqual(0, repository.ApplyCalls);
    }

    [TestMethod]
    public async Task ReadLifetime_DerivesLevelWithoutPersistingRankState()
    {
        var repository = new MemoryGrantRepository(
            lifetime: new ProgressionLifetimeState(Player, 249, 4));
        var service = new ProgressionGrantService(repository, Definitions());

        var state = await service.ReadLifetimeAsync(Player);

        Assert.AreEqual(249L, state.LifetimeXp);
        Assert.AreEqual(4L, state.Revision);
    }

    private static ProgressionDefinitionSnapshot Definitions()
        => ProgressionDefinitionSnapshot.Create(
            [new(1, 0), new(2, 100), new(3, 250)],
            [new("double-weekend", Friday, Friday.AddDays(2), 2m)]);

    private sealed class MemoryGrantRepository : IProgressionGrantRepository
    {
        private ProgressionGrantRecord? _grant;
        private ProgressionLifetimeState _lifetime;

        public MemoryGrantRepository(
            ProgressionGrantRecord? grant = null,
            ProgressionLifetimeState? lifetime = null)
        {
            _grant = grant;
            _lifetime = lifetime
                ?? (grant is null
                    ? new ProgressionLifetimeState(Player, 0, 0)
                    : new ProgressionLifetimeState(
                        Player, grant.LifetimeXpAfter, grant.AccountRevisionAfter));
        }

        public int ApplyCalls { get; private set; }

        public ValueTask<ProgressionLifetimeState> ReadLifetimeAsync(
            PlayerId playerId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_lifetime);

        public ValueTask<ProgressionGrantRecord?> ReadGrantAsync(
            PlayerId playerId,
            string grantId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(
                _grant is not null && _grant.GrantId == grantId ? _grant : null);

        public ValueTask<ProgressionGrantCommitResult> ApplyAsync(
            PlayerId playerId,
            ProgressionGrantCandidate candidate,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            var after = checked(_lifetime.LifetimeXp + candidate.AwardedXp);
            var revision = checked(_lifetime.Revision + 1);
            _grant = new ProgressionGrantRecord(
                playerId, candidate.GrantId, candidate.Source, candidate.BaseXp,
                candidate.AwardedXp, candidate.Reason, candidate.OccurredAtUtc,
                candidate.BoostId, candidate.BoostMultiplier, after, revision);
            _lifetime = new ProgressionLifetimeState(playerId, after, revision);
            return ValueTask.FromResult(new ProgressionGrantCommitResult(true, _grant));
        }
    }
}
