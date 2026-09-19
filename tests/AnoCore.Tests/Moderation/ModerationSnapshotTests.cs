using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Moderation;

namespace AnoCore.Tests.Moderation;

[TestClass]
public sealed class ModerationSnapshotTests
{
    private static readonly PlayerId Target = new(76561198000004001);
    private static readonly PlayerId Admin = new(76561198000004002);
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 16, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void Snapshot_StartsAsCacheMiss()
    {
        var service = new ModerationService(new MemoryRepository());

        var found = ((IModerationSnapshotProvider)service).TryGetRestrictions(
            Target,
            Now,
            out var restrictions);

        Assert.IsFalse(found);
        Assert.AreEqual(ModerationRestriction.None, restrictions);
    }

    [TestMethod]
    public async Task GetStateAsync_LoadsSnapshotAndEvaluatesExpiryWithoutAnotherRepositoryRead()
    {
        var expires = Now.AddMinutes(5);
        var repository = new MemoryRepository(
            new ModerationSanction(
                Guid.NewGuid(),
                Target,
                Admin,
                ModerationRestriction.Voice,
                "temporary mute",
                Now,
                expires));
        var service = new ModerationService(repository);

        var loaded = await service.GetStateAsync(Target, Now);
        Assert.AreEqual(ModerationRestriction.Voice, loaded.Restrictions);
        Assert.AreEqual(1, repository.ActiveReads);

        var snapshots = (IModerationSnapshotProvider)service;
        Assert.IsTrue(snapshots.TryGetRestrictions(Target, expires.AddTicks(-1), out var beforeExpiry));
        Assert.AreEqual(ModerationRestriction.Voice, beforeExpiry);
        Assert.IsTrue(snapshots.TryGetRestrictions(Target, expires, out var atExpiry));
        Assert.AreEqual(ModerationRestriction.None, atExpiry);
        Assert.AreEqual(1, repository.ActiveReads);
    }

    [TestMethod]
    public async Task ApplyAsync_UpdatesAnAlreadyLoadedSnapshotAfterPersistenceSucceeds()
    {
        var repository = new MemoryRepository();
        var service = new ModerationService(repository);
        await service.GetStateAsync(Target, Now);

        await service.ApplyAsync(
            Target,
            Admin,
            ModerationRestriction.Chat,
            "gag",
            Now.AddMinutes(1));

        Assert.IsTrue(((IModerationSnapshotProvider)service).TryGetRestrictions(
            Target,
            Now.AddMinutes(2),
            out var restrictions));
        Assert.AreEqual(ModerationRestriction.Chat, restrictions);
    }

    [TestMethod]
    public async Task RevokeAsync_UpdatesAnAlreadyLoadedSnapshotWithoutDroppingOtherRestrictions()
    {
        var voice = new ModerationSanction(
            Guid.NewGuid(),
            Target,
            Admin,
            ModerationRestriction.Voice,
            "voice",
            Now);
        var chat = new ModerationSanction(
            Guid.NewGuid(),
            Target,
            Admin,
            ModerationRestriction.Chat,
            "chat",
            Now);
        var repository = new MemoryRepository(voice, chat);
        var service = new ModerationService(repository);
        await service.GetStateAsync(Target, Now);

        await service.RevokeAsync(
            Target,
            Admin,
            ModerationRestriction.Voice,
            "clear voice",
            Now.AddMinutes(1));

        Assert.IsTrue(((IModerationSnapshotProvider)service).TryGetRestrictions(
            Target,
            Now.AddMinutes(2),
            out var restrictions));
        Assert.AreEqual(ModerationRestriction.Chat, restrictions);
    }

    [TestMethod]
    public async Task Invalidate_RemovesOnlyRequestedPlayerSnapshot()
    {
        var other = new PlayerId(76561198000004003);
        var repository = new MemoryRepository(
            new ModerationSanction(Guid.NewGuid(), Target, Admin, ModerationRestriction.Chat, "one", Now),
            new ModerationSanction(Guid.NewGuid(), other, Admin, ModerationRestriction.Voice, "two", Now));
        var service = new ModerationService(repository);
        var snapshots = (IModerationSnapshotProvider)service;

        await service.GetStateAsync(Target, Now);
        await service.GetStateAsync(other, Now);
        snapshots.Invalidate(Target);

        Assert.IsFalse(snapshots.TryGetRestrictions(Target, Now, out _));
        Assert.IsTrue(snapshots.TryGetRestrictions(other, Now, out var otherRestrictions));
        Assert.AreEqual(ModerationRestriction.Voice, otherRestrictions);
    }

    [TestMethod]
    public async Task ApplyAsync_PersistenceFailureLeavesLoadedSnapshotUnchanged()
    {
        var existing = new ModerationSanction(
            Guid.NewGuid(),
            Target,
            Admin,
            ModerationRestriction.Voice,
            "existing",
            Now);
        var repository = new MemoryRepository(existing) { FailAdds = true };
        var service = new ModerationService(repository);
        var snapshots = (IModerationSnapshotProvider)service;
        await service.GetStateAsync(Target, Now);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await service.ApplyAsync(
                Target,
                Admin,
                ModerationRestriction.Chat,
                "must fail",
                Now.AddMinutes(1)));

        Assert.IsTrue(snapshots.TryGetRestrictions(Target, Now.AddMinutes(2), out var restrictions));
        Assert.AreEqual(ModerationRestriction.Voice, restrictions);
    }

    [TestMethod]
    public async Task RevokeAsync_PersistenceFailureLeavesLoadedSnapshotUnchanged()
    {
        var existing = new ModerationSanction(
            Guid.NewGuid(),
            Target,
            Admin,
            ModerationRestriction.Chat,
            "existing",
            Now);
        var repository = new MemoryRepository(existing) { FailRevokes = true };
        var service = new ModerationService(repository);
        var snapshots = (IModerationSnapshotProvider)service;
        await service.GetStateAsync(Target, Now);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await service.RevokeAsync(
                Target,
                Admin,
                ModerationRestriction.Chat,
                "must fail",
                Now.AddMinutes(1)));

        Assert.IsTrue(snapshots.TryGetRestrictions(Target, Now.AddMinutes(2), out var restrictions));
        Assert.AreEqual(ModerationRestriction.Chat, restrictions);
    }

    [TestMethod]
    public async Task CancelledMutationDoesNotTouchRepositoryOrLoadedSnapshot()
    {
        var existing = new ModerationSanction(
            Guid.NewGuid(),
            Target,
            Admin,
            ModerationRestriction.Voice,
            "existing",
            Now);
        var repository = new MemoryRepository(existing);
        var service = new ModerationService(repository);
        var snapshots = (IModerationSnapshotProvider)service;
        await service.GetStateAsync(Target, Now);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await service.ApplyAsync(
                Target,
                Admin,
                ModerationRestriction.Chat,
                "cancelled",
                Now.AddMinutes(1),
                cancellationToken: cancellation.Token));

        Assert.AreEqual(0, repository.AddCalls);
        Assert.IsTrue(snapshots.TryGetRestrictions(Target, Now.AddMinutes(2), out var restrictions));
        Assert.AreEqual(ModerationRestriction.Voice, restrictions);
    }

    private sealed class MemoryRepository(params ModerationSanction[] initial) : IModerationRepository
    {
        private readonly List<ModerationSanction> _sanctions = [.. initial];

        public int ActiveReads { get; private set; }

        public int AddCalls { get; private set; }

        public bool FailAdds { get; init; }

        public bool FailRevokes { get; init; }

        public ValueTask AddAsync(
            IReadOnlyCollection<ModerationSanction> sanctions,
            ModerationAuditEntry audit,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddCalls++;
            if (FailAdds)
            {
                throw new InvalidOperationException("Simulated add failure.");
            }

            _sanctions.AddRange(sanctions);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<ModerationSanction>> GetActiveAsync(
            PlayerId targetId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActiveReads++;
            IReadOnlyList<ModerationSanction> result = _sanctions
                .Where(value => value.TargetId == targetId && value.IsActiveAt(atUtc))
                .ToArray();
            return ValueTask.FromResult(result);
        }

        public ValueTask<IReadOnlyList<ModerationSanction>> GetHistoryAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ModerationSanction>>(
                _sanctions.Where(value => value.TargetId == targetId).ToArray());

        public ValueTask<IReadOnlyList<ModerationSanction>> RevokeActiveAsync(
            PlayerId targetId,
            ModerationRestriction restrictions,
            PlayerId? actorId,
            string reason,
            DateTimeOffset atUtc,
            ModerationAuditEntry audit,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailRevokes)
            {
                throw new InvalidOperationException("Simulated revoke failure.");
            }

            var changed = new List<ModerationSanction>();
            for (var index = 0; index < _sanctions.Count; index++)
            {
                var current = _sanctions[index];
                if (current.TargetId != targetId
                    || !current.IsActiveAt(atUtc)
                    || !restrictions.HasFlag(current.Restriction))
                {
                    continue;
                }

                var revoked = current.Revoke(actorId, reason, atUtc);
                _sanctions[index] = revoked;
                changed.Add(revoked);
            }

            return ValueTask.FromResult<IReadOnlyList<ModerationSanction>>(changed);
        }

        public ValueTask<IReadOnlyList<ModerationAuditEntry>> GetAuditHistoryAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ModerationAuditEntry>>([]);
    }
}
