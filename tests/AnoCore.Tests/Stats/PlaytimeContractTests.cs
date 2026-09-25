using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class PlaytimeContractTests
{
    private static readonly PlayerId Player = new(76561198000011101);
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 23, 59, 30, TimeSpan.Zero);

    [TestMethod]
    public void Session_RejectsInvalidRangesAndCountsOnlyAccountedTime()
    {
        var session = new PlaytimeSession(Player, PlayerSessionId.New(), Start, Start.AddSeconds(10));
        Assert.AreEqual(TimeSpan.FromSeconds(10), session.Accounted);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlaytimeSession(Player, PlayerSessionId.New(), Start, Start.AddTicks(-1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlaytimeSession(Player, PlayerSessionId.New(), Start, Start.AddSeconds(10), Start.AddSeconds(9)));
    }

    [TestMethod]
    public void Session_ClipsDayBoundaryWithoutCountingAfterCheckpoint()
    {
        var session = new PlaytimeSession(Player, PlayerSessionId.New(), Start, Start.AddMinutes(2));
        Assert.AreEqual(TimeSpan.FromSeconds(30),
            session.AccountedWithin(new DateOnly(2026, 9, 25)));
        Assert.AreEqual(TimeSpan.FromSeconds(90),
            session.AccountedWithin(new DateOnly(2026, 9, 26)));
        Assert.AreEqual(TimeSpan.Zero, session.AccountedWithin(new DateOnly(2026, 9, 27)));
    }
}
