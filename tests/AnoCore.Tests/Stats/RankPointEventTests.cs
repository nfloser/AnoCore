using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class RankPointEventTests
{
    private static readonly PlayerId Player = new(76561198000278101);

    [TestMethod]
    public void Batch_NormalizesTimestampAndSnapshotsSortedUniqueAwards()
    {
        var other = new PlayerId(Player.SteamId64 + 1);
        var awards = new List<RankPointAward> { new(other, -10), new(Player, 25) };
        var at = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.FromHours(2)).AddTicks(19);
        var batch = RankPointEventBatch.Create(Guid.NewGuid(), "combat.death", at, awards);
        awards.Clear();
        Assert.AreEqual(TimeSpan.Zero, batch.OccurredAtUtc.Offset);
        Assert.AreEqual(0L, batch.OccurredAtUtc.Ticks % 10);
        Assert.AreEqual(Player, batch.Awards[0].PlayerId);
        Assert.AreEqual(-10L, batch.Awards[1].Points);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<RankPointAward>)batch.Awards).Clear());
    }

    [TestMethod]
    public void Batch_RejectsInvalidIdentitySourceAndUnboundedAwards()
    {
        var id = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        Assert.ThrowsExactly<ArgumentException>(() => RankPointEventBatch.Create(Guid.Empty, "kill", at, [new(Player, 1)]));
        foreach (var source in new[] { "", " leading", "bad\n", "with space", new string('a', 65) })
            Assert.ThrowsExactly<ArgumentException>(() => RankPointEventBatch.Create(id, source, at, [new(Player, 1)]));
        foreach (var awards in new RankPointAward[][]
        {
            [], [null!], [new(null!, 1)], [new(Player, 1), new(Player, 2)],
            [new(Player, RankPointEventBatch.MaximumAbsolutePoints + 1)],
            [new(Player, -RankPointEventBatch.MaximumAbsolutePoints - 1)],
        })
            Assert.ThrowsExactly<ArgumentException>(() => RankPointEventBatch.Create(id, "kill", at, awards));
        Assert.ThrowsExactly<ArgumentException>(() => RankPointEventBatch.Create(id, "kill", at,
            Enumerable.Range(0, RankPointEventBatch.MaximumAwards + 1)
                .Select(index => new RankPointAward(new PlayerId(Player.SteamId64 + (ulong)index), 1))));
    }

    [TestMethod]
    public void ScoreMode_IsExplicitAndLegacyConstructorsRemainDerived()
    {
        Assert.AreEqual(RankScoreSource.DerivedStatistics, new RankScoreWeights(2, 1, 1).Source);
        Assert.AreEqual(RankScoreSource.DerivedStatistics, new RankScoreWeights(2, 1, 1, 10).Source);
        var ledger = new RankScoreWeights(2, 1, 1, 10, null, RankScoreSource.EventLedger);
        Assert.AreEqual(RankScoreSource.EventLedger, ledger.Source);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RankScoreWeights(2, 1, 1, 0, null, (RankScoreSource)255));
    }
}
