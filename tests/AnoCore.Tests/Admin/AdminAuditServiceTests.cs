using AnoCore.Abstractions.Auditing;
using AnoCore.Abstractions.Players;
using AnoCore.Runtime.Auditing;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class AdminAuditServiceTests
{
    private static readonly PlayerId Target = new(76561198000008002);
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task RecordAsync_PersistsNormalizedEntryAndReturnsIt()
    {
        var repository = new MemoryRepository();
        var service = new AdminAuditService(repository);
        var action = new AdminActionId("kick.silent");

        var recorded = await service.RecordAsync(action, null, Target, "  repeated griefing  ", Now);

        Assert.AreNotEqual(Guid.Empty, recorded.Id);
        Assert.AreEqual(action, recorded.Action);
        Assert.AreEqual("repeated griefing", recorded.Reason);
        Assert.AreEqual(recorded, repository.Entries.Single());
    }

    [TestMethod]
    public async Task Queries_ValidateLimitsAndOrderByTimeThenId()
    {
        var repository = new MemoryRepository();
        var service = new AdminAuditService(repository);
        var first = new AdminAuditEntry(Guid.Parse("00000000-0000-0000-0000-000000000001"), new AdminActionId("kick"), null, Target, "first", Now);
        var second = new AdminAuditEntry(Guid.Parse("00000000-0000-0000-0000-000000000002"), new AdminActionId("warn"), null, Target, "second", Now);
        repository.Entries.Add(second);
        repository.Entries.Add(first);

        CollectionAssert.AreEqual(new[] { first, second }, (await service.GetTargetHistoryAsync(Target)).ToArray());
        CollectionAssert.AreEqual(new[] { first, second }, (await service.GetRecentAsync()).ToArray());
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () => await service.GetRecentAsync(0));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () => await service.GetTargetHistoryAsync(Target, 1001));
    }

    private sealed class MemoryRepository : IAdminAuditRepository
    {
        public List<AdminAuditEntry> Entries { get; } = [];

        public ValueTask AppendAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entries.Add(entry);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<AdminAuditEntry>>(Entries.Take(limit).ToArray());

        public ValueTask<IReadOnlyList<AdminAuditEntry>> GetTargetHistoryAsync(PlayerId targetId, int limit = 100, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<AdminAuditEntry>>(Entries.Where(entry => entry.TargetId == targetId).Take(limit).ToArray());
    }
}
