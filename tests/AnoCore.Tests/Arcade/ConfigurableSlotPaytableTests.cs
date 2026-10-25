using System.Text.Json;
using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class ConfigurableSlotPaytableTests
{
    [TestMethod]
    public void BiggestBoySupportsEditableLineDivisorAndEverySymbolPayout()
    {
        var grid = Enumerable.Repeat(BiggestBoySymbol.Scatter, 15).ToArray();
        grid[0] = grid[3] = grid[6] = grid[9] = grid[12] = BiggestBoySymbol.Kebap;
        Assert.AreEqual(500L, BiggestBoyBalancedRules.EvaluateGrid(100, grid).Payout);
        Assert.AreEqual(5000L, BiggestBoyBalancedRules.EvaluateGrid(100, grid,
            new SlotPaytableSettings(1)).Payout);
        var rows = Enumerable.Range(0, 10).Select(_ => new[] { 1, 2, 3 }).ToArray();
        rows[0] = [3, 7, 20];
        var configured = new SlotPaytableSettings(1, rows).Validate(10);
        Assert.AreEqual(2000L, BiggestBoyBalancedRules.EvaluateGrid(100, grid, configured).Payout);
        Assert.AreEqual(620000L, BiggestBoyBalancedRules.MaximumRoundExposure(100, configured));
        Assert.AreEqual(20000L, BiggestBoyBalancedRules.EvaluateGrid(100,
            Enumerable.Repeat(BiggestBoySymbol.Kebap, 15).ToArray(), configured).Payout);
    }

    [TestMethod]
    public void BookUsesItsOwnSevenRowTableAndCorrectReservation()
    {
        var rows = Enumerable.Range(0, 7).Select(_ => new[] { 1, 2, 3 }).ToArray();
        rows[6] = [6, 12, 30];
        var configured = new SlotPaytableSettings(1, rows).Validate(7);
        var grid = Enumerable.Repeat(BonusSlotSymbol.Seven, 15).ToArray();
        Assert.AreEqual(5000L, BookBalancedRules.EvaluateGrid(100, grid).Payout);
        Assert.AreEqual(30000L, BookBalancedRules.EvaluateGrid(100, grid, configured).Payout);
        Assert.AreEqual(930000L, BookBalancedRules.MaximumRoundExposure(100, configured));
        Assert.AreEqual(155000L, BookBalancedRules.MaximumRoundExposure(100));
    }

    [TestMethod]
    public void RejectsMalformedOrUnboundedConfigBeforeAnyRoundStarts()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SlotPaytableSettings(3).Validate(10));
        Assert.ThrowsExactly<ArgumentException>(() => new SlotPaytableSettings(10,
            [new[] { 1, 2, 3 }]).Validate(10));
        Assert.ThrowsExactly<ArgumentException>(() => new SlotPaytableSettings(10,
            Enumerable.Range(0, 10).Select(_ => new[] { 1, 2 }).ToArray()).Validate(10));
        Assert.ThrowsExactly<ArgumentException>(() => new SlotPaytableSettings(10,
            Enumerable.Range(0, 10).Select(_ => new[] { 0, 2, 3 }).ToArray()).Validate(10));
        Assert.ThrowsExactly<ArgumentException>(() => new SlotPaytableSettings(10,
            Enumerable.Range(0, 10).Select(_ => new[] { 1, 2, 10001 }).ToArray()).Validate(10));
    }

    [TestMethod]
    public void SerializedControlledBonusRetainsItsOriginalDivisorAndMultipliers()
    {
        var rows = Enumerable.Range(0, 10).Select(_ => new[] { 2, 5, 15 }).ToArray();
        var settings = new SlotOutcomeSettings(100, 100, 0, new SlotPaytableSettings(1, rows));
        var first = ControlledSlotRules.Biggest(100, _ => 0, settings);
        var saved = JsonSerializer.Deserialize<BiggestBoySpinResult>(JsonSerializer.Serialize(first))!;
        Assert.AreEqual(1, saved.State.OutcomeSettings!.Paytable!.LineStakeDivisor);
        Assert.AreEqual(15, saved.State.OutcomeSettings.Paytable.Multipliers![0][2]);
        var changed = new SlotOutcomeSettings(0, 0, 0, new SlotPaytableSettings(10));
        var resumed = ControlledSlotRules.Biggest(100, _ => 0, changed, saved.State);
        Assert.AreEqual(1, resumed.State.OutcomeSettings!.Paytable!.LineStakeDivisor);
        Assert.AreEqual(15, resumed.State.OutcomeSettings.Paytable.Multipliers![0][2]);
    }
}
