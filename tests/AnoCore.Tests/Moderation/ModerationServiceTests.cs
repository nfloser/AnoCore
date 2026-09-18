using AnoCore.Abstractions.Moderation;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Moderation;

namespace AnoCore.Tests.Moderation;

[TestClass]
public sealed class ModerationServiceTests
{
    private static readonly PlayerId Target = new(76561198000002001);
    private static readonly PlayerId Admin = new(76561198000002002);
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ApplyAsync_PermanentVoiceRestrictionProducesActiveStateAndAudit()
    {
        var repository = new MemoryRepository();
        var service = new ModerationService(repository);

        var created = await service.ApplyAsync(
            Target,
            Admin,
            ModerationRestriction.Voice,
            "voice abuse",
            Now);

        Assert.AreEqual(1, created.Count);
        Assert.AreEqual(ModerationRestriction.Voice, created.Single().Restriction);
        Assert.IsNull(created.Single().ExpiresAtUtc);

        var state = await service.GetStateAsync(Target, Now.AddHours(1));
        Assert.AreEqual(ModerationRestriction.Voice, state.Restrictions);
        Assert.AreEqual(1, state.ActiveSanctions.Count);

        var audit = await service.GetAuditHistoryAsync(Target);
        Assert.AreEqual(1, audit.Count);
        Assert.AreEqual(ModerationAuditAction.Applied, audit[0].Action);
        Assert.AreEqual(Admin, audit[0].ActorId);
        Assert.AreEqual("voice abuse", audit[0].Reason);
    }

    [TestMethod]
    public async Task ApplyAsync_TemporaryRestrictionExpiresExactlyAtBoundary()
    {
        var service = new ModerationService(new MemoryRepository());
        var expires = Now.AddMinutes(10);

        await service.ApplyAsync(
            Target,
            Admin,
            ModerationRestriction.Chat,
            "spam",
            Now,
            expires);

        Assert.AreEqual(
            ModerationRestriction.Chat,
            (await service.GetStateAsync(Target, expires.AddTicks(-1))).Restrictions);
        Assert.AreEqual(
            ModerationRestriction.None,
            (await service.GetStateAsync(Target, expires)).Restrictions);
    }

    [TestMethod]
    public async Task ApplyAsync_SilenceExpandsToVoiceAndChatAndSupportsPartialRevoke()
    {
        var service = new ModerationService(new MemoryRepository());

        var created = await service.ApplyAsync(
            Target,
            Admin,
            ModerationRestriction.Voice | ModerationRestriction.Chat,
            "toxicity",
            Now);

        CollectionAssert.AreEquivalent(
            new[] { ModerationRestriction.Voice, ModerationRestriction.Chat },
            created.Select(value => value.Restriction).ToArray());

        var revoked = await service.RevokeAsync(
            Target,
            Admin,
            ModerationRestriction.Voice,
            "mute lifted",
            Now.AddMinutes(5));

        Assert.AreEqual(1, revoked.Count);
        Assert.AreEqual(ModerationRestriction.Voice, revoked.Single().Restriction);
        Assert.AreEqual(
            ModerationRestriction.Chat,
            (await service.GetStateAsync(Target, Now.AddMinutes(6))).Restrictions);
    }

    [TestMethod]
    public async Task OverlappingRestrictionsRemainActiveUntilEveryGrantExpiresOrIsRevoked()
    {
        var service = new ModerationService(new MemoryRepository());

        await service.ApplyAsync(
            Target,
            Admin,
            ModerationRestriction.Voice,
            "short mute",
            Now,
            Now.AddMinutes(5));
        await service.ApplyAsync(
            Target,
            Admin,
            ModerationRestriction.Voice,
            "long mute",
            Now.AddMinutes(1),
            Now.AddMinutes(30));

        var afterFirstExpiry = await service.GetStateAsync(Target, Now.AddMinutes(6));
        Assert.AreEqual(ModerationRestriction.Voice, afterFirstExpiry.Restrictions);
        Assert.AreEqual(1, afterFirstExpiry.ActiveSanctions.Count);

        var revoked = await service.RevokeAsync(
            Target,
            null,
            ModerationRestriction.Voice,
            "console override",
            Now.AddMinutes(7));

        Assert.AreEqual(1, revoked.Count);
        Assert.AreEqual(
            ModerationRestriction.None,
            (await service.GetStateAsync(Target, Now.AddMinutes(8))).Restrictions);
    }

    [TestMethod]
    public async Task ApplyAndRevoke_AllowOfflineTargetAndConsoleActorButRequireReasonAndValidExpiry()
    {
        var service = new ModerationService(new MemoryRepository());

        var ban = await service.ApplyAsync(
            Target,
            null,
            ModerationRestriction.Connect,
            "offline ban",
            Now);

        Assert.AreEqual(ModerationRestriction.Connect, ban.Single().Restriction);
        Assert.IsNull(ban.Single().ActorId);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await service.ApplyAsync(Target, Admin, ModerationRestriction.Chat, " ", Now));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await service.ApplyAsync(Target, Admin, ModerationRestriction.Chat, "spam", Now, Now));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await service.ApplyAsync(Target, Admin, ModerationRestriction.None, "invalid", Now));
    }

    [TestMethod]
    public void ModerationSanction_RevokeCreatesValidatedCopyAndPreservesOriginal()
    {
        var original = new ModerationSanction(
            Guid.NewGuid(),
            Target,
            Admin,
            ModerationRestriction.Chat,
            "spam",
            Now,
            Now.AddMinutes(10));

        var revoked = original.Revoke(Admin, "resolved", Now.AddMinutes(5));

        Assert.IsNull(original.RevokedAtUtc);
        Assert.AreEqual(Now.AddMinutes(5), revoked.RevokedAtUtc);
        Assert.AreEqual(Admin, revoked.RevokedById);
        Assert.AreEqual("resolved", revoked.RevocationReason);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            original.Revoke(Admin, "too late", Now.AddMinutes(10)));
    }

    [TestMethod]
    public void ModerationSanction_RejectsRevocationMetadataWithoutRevocationTime()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ModerationSanction(
            Guid.NewGuid(),
            Target,
            Admin,
            ModerationRestriction.Chat,
            "spam",
            Now,
            revokedById: Admin));

        Assert.ThrowsExactly<ArgumentException>(() => new ModerationSanction(
            Guid.NewGuid(),
            Target,
            Admin,
            ModerationRestriction.Chat,
            "spam",
            Now,
            revocationReason: "orphaned reason"));
    }

    [TestMethod]
    public async Task ApplyAsync_RejectsReasonLongerThanPersistenceSchema()
    {
        var service = new ModerationService(new MemoryRepository());

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await service.ApplyAsync(
                Target,
                Admin,
                ModerationRestriction.Chat,
                new string('x', ModerationValidation.MaxReasonLength + 1),
                Now));
    }

    [TestMethod]
    public async Task HistoryAndAuditAreDeterministicallyOrderedAndNeverDeletedByRevoke()
    {
        var service = new ModerationService(new MemoryRepository());

        await service.ApplyAsync(Target, Admin, ModerationRestriction.Chat, "first", Now);
        await service.ApplyAsync(Target, Admin, ModerationRestriction.Voice, "second", Now.AddMinutes(1));
        await service.RevokeAsync(Target, Admin, ModerationRestriction.Chat, "resolved", Now.AddMinutes(2));

        var history = await service.GetHistoryAsync(Target);
        var audit = await service.GetAuditHistoryAsync(Target);

        Assert.AreEqual(2, history.Count);
        Assert.AreEqual("first", history[0].Reason);
        Assert.IsNotNull(history[0].RevokedAtUtc);
        Assert.AreEqual("second", history[1].Reason);

        Assert.AreEqual(3, audit.Count);
        CollectionAssert.AreEqual(
            new[] { ModerationAuditAction.Applied, ModerationAuditAction.Applied, ModerationAuditAction.Revoked },
            audit.Select(value => value.Action).ToArray());
    }

    private sealed class MemoryRepository : IModerationRepository
    {
        private readonly List<ModerationSanction> _sanctions = [];
        private readonly List<ModerationAuditEntry> _audit = [];

        public ValueTask AddAsync(
            IReadOnlyCollection<ModerationSanction> sanctions,
            ModerationAuditEntry audit,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _sanctions.AddRange(sanctions);
            _audit.Add(audit);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<ModerationSanction>> GetActiveAsync(
            PlayerId targetId,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ModerationSanction> result = _sanctions
                .Where(value => value.TargetId == targetId && value.IsActiveAt(atUtc))
                .ToArray();
            return ValueTask.FromResult(result);
        }

        public ValueTask<IReadOnlyList<ModerationSanction>> GetHistoryAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ModerationSanction> result = _sanctions
                .Where(value => value.TargetId == targetId)
                .ToArray();
            return ValueTask.FromResult(result);
        }

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

            if (changed.Count > 0)
            {
                _audit.Add(audit);
            }

            return ValueTask.FromResult<IReadOnlyList<ModerationSanction>>(changed);
        }

        public ValueTask<IReadOnlyList<ModerationAuditEntry>> GetAuditHistoryAsync(
            PlayerId targetId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ModerationAuditEntry> result = _audit
                .Where(value => value.TargetId == targetId)
                .ToArray();
            return ValueTask.FromResult(result);
        }
    }
}
