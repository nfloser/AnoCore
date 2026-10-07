using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class RankScoreboardTests
{
    [TestMethod]
    public void ScoreboardMapping_ClampsNativeScoresAndMapsExplicitModes()
    {
        var config = new RankConfiguration { Scoreboard = new() { SyncScore = true, RankMode = RankScoreboardMode.Premier } };
        var value = RankScoreboardProjection.Create(config, long.MaxValue);
        Assert.AreEqual(int.MaxValue, value.Score);
        Assert.AreEqual(int.MaxValue, value.Badge!.Ranking);
        Assert.AreEqual((sbyte)11, value.Badge.Type);
        config.Scoreboard.RankMode = RankScoreboardMode.Competitive;
        value = RankScoreboardProjection.Create(config, 100);
        Assert.AreEqual(3, value.Badge!.Ranking);
        Assert.AreEqual((sbyte)12, value.Badge.Type);
        config.Scoreboard.RankMode = RankScoreboardMode.Wingman;
        Assert.AreEqual((sbyte)7, RankScoreboardProjection.Create(config, 100).Badge!.Type);
        config.Scoreboard.RankMode = RankScoreboardMode.DangerZone;
        Assert.AreEqual((sbyte)10, RankScoreboardProjection.Create(config, 100).Badge!.Type);
        config.Scoreboard = new();
        value = RankScoreboardProjection.Create(config, 100);
        Assert.IsNull(value.Score);
        Assert.IsNull(value.Badge);
    }

    [TestMethod]
    public void NativeOwnership_RestoresOriginalOnlyWhileValuesRemainOwned()
    {
        var owned = new OwnedRankScoreboardValue<int>();
        Assert.IsTrue(owned.TryApply(7, 10));
        Assert.IsTrue(owned.TryApply(10, 20));
        Assert.IsTrue(owned.TryRestore(20, out var original));
        Assert.AreEqual(7, original);
        var changed = new OwnedRankScoreboardValue<int>();
        Assert.IsTrue(changed.TryApply(7, 10));
        Assert.IsFalse(changed.TryApply(99, 20));
        Assert.IsFalse(changed.TryRestore(99, out _));
        Assert.IsFalse(changed.TryApply(99, 30));
        var score = new OwnedRankScoreboardValue<int>();
        Assert.IsTrue(score.TryApply(7, 10, replaceExternal: true));
        Assert.IsTrue(score.TryApply(11, 20, replaceExternal: true));
        Assert.IsTrue(score.TryRestore(20, out var latestExternal));
        Assert.AreEqual(11, latestExternal);
    }

    [TestMethod]
    public void Configuration_RejectsUnknownModesAndKeepsUpgradeDefaultsDisabled()
    {
        Assert.IsFalse(new RankConfiguration().Scoreboard.Enabled);
        Assert.IsNotEmpty(RankConfiguration.Validate(new RankConfiguration { Scoreboard = null! }));
        Assert.IsNotEmpty(RankConfiguration.Validate(new RankConfiguration { Scoreboard = new() { RankMode = (RankScoreboardMode)255 } }));
    }
}
