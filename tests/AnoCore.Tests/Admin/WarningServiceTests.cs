using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Warnings;
using AnoCore.Runtime.Warnings;

namespace AnoCore.Tests.Admin;

[TestClass]
public sealed class WarningServiceTests
{
    private static readonly PlayerId Target = new(76561198000009101);
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task WarnAndClear_PreserveHistoricalEntryAndValidateBounds()
    {
        var repository = new MemoryWarnings();
        var service = new WarningService(repository);
        var first = await service.WarnAsync(Target, null, "spam", Now, Now.AddMinutes(15));
        var second = await service.WarnAsync(Target, null, "toxicity", Now.AddMinutes(1));
        Assert.AreEqual(2, (await service.GetActiveAsync(Target, Now.AddMinutes(2))).Count);

        var cleared = await service.ClearAsync(Target, null, "resolved", Now.AddMinutes(3));
        Assert.AreEqual(2, cleared.Count);
        Assert.AreEqual(first.Id, cleared[0].Id);
        Assert.AreEqual(second.Id, cleared[1].Id);
        Assert.AreEqual(0, (await service.GetActiveAsync(Target, Now.AddMinutes(3))).Count);
        Assert.AreEqual(2, (await service.GetHistoryAsync(Target)).Count);
        Assert.AreEqual(0, (await service.ClearAsync(Target, null, "again", Now.AddMinutes(4))).Count);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await service.GetHistoryAsync(Target, 0));
    }

    private sealed class MemoryWarnings : IWarningRepository
    {
        private readonly List<WarningRecord> _items = [];

        public ValueTask InsertAsync(WarningRecord warning, CancellationToken cancellationToken = default)
        {
            _items.Add(warning);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<WarningRecord>> GetActiveAsync(
            PlayerId targetId, DateTimeOffset atUtc, int limit = 100,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<WarningRecord>>(_items
                .Where(value => value.TargetId == targetId && value.IsActiveAt(atUtc))
                .Take(limit).ToArray());

        public ValueTask<IReadOnlyList<WarningRecord>> GetHistoryAsync(
            PlayerId targetId, int limit = 100, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<WarningRecord>>(_items
                .Where(value => value.TargetId == targetId).Take(limit).ToArray());

        public ValueTask<IReadOnlyList<WarningRecord>> ClearActiveAsync(
            PlayerId targetId, PlayerId? actorId, string reason, DateTimeOffset atUtc,
            CancellationToken cancellationToken = default)
        {
            var changed = new List<WarningRecord>();
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].TargetId != targetId || !_items[i].IsActiveAt(atUtc)) continue;
                var replacement = _items[i].Clear(actorId, reason, atUtc);
                _items[i] = replacement;
                changed.Add(replacement);
            }

            return ValueTask.FromResult<IReadOnlyList<WarningRecord>>(changed);
        }
    }
}
