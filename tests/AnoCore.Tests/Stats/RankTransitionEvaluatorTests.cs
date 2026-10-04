using AnoCore.Modules.Stats;

namespace AnoCore.Tests.Stats;

[TestClass]
public sealed class RankTransitionEvaluatorTests
{
    private static readonly RankConfiguration Configuration = new()
    {
        Thresholds =
        [
            new RankThreshold("Recruit", 0),
            new RankThreshold("Veteran", 10),
            new RankThreshold("Elite", 100),
        ],
    };

    [TestMethod]
    public void Evaluate_ReportsExactBoundaryPromotionAndMultiRankJump()
    {
        var exact = RankTransitionEvaluator.Evaluate(Configuration, 9, 10);
        Assert.IsNotNull(exact);
        Assert.AreEqual(RankTransitionKind.Promotion, exact.Kind);
        Assert.AreEqual("Recruit", exact.Previous.Name);
        Assert.AreEqual("Veteran", exact.Current.Name);

        var jump = RankTransitionEvaluator.Evaluate(Configuration, 0, 100);
        Assert.IsNotNull(jump);
        Assert.AreEqual(RankTransitionKind.Promotion, jump.Kind);
        Assert.AreEqual("Elite", jump.Current.Name);
    }

    [TestMethod]
    public void Evaluate_ReportsDemotionAndIgnoresMovementInsideRank()
    {
        var demotion = RankTransitionEvaluator.Evaluate(Configuration, 100, 9);
        Assert.IsNotNull(demotion);
        Assert.AreEqual(RankTransitionKind.Demotion, demotion.Kind);
        Assert.AreEqual("Elite", demotion.Previous.Name);
        Assert.AreEqual("Recruit", demotion.Current.Name);
        Assert.IsNull(RankTransitionEvaluator.Evaluate(Configuration, 20, 99));
    }

    [TestMethod]
    public void Evaluate_RejectsNegativePointInputs()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => RankTransitionEvaluator.Evaluate(Configuration, -1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => RankTransitionEvaluator.Evaluate(Configuration, 0, -1));
    }
}
