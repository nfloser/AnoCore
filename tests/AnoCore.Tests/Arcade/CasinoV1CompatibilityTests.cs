using System.Text.Json;
using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class CasinoV1CompatibilityTests
{
    [TestMethod]
    public void LegacyEnumValuesRulesVersionAndSymbolOrderArePinned()
    {
        var games = new (string Name, CasinoGame Value, int Expected)[]
        {
            (nameof(CasinoGame.Slots), CasinoGame.Slots, 0),
            (nameof(CasinoGame.Coinflip), CasinoGame.Coinflip, 1),
            (nameof(CasinoGame.Roulette), CasinoGame.Roulette, 2),
        };
        foreach (var game in games)
            Assert.AreEqual(game.Expected, Convert.ToInt32(game.Value), game.Name);

        var actions = new (string Name, CasinoAction Value, int Expected)[]
        {
            (nameof(CasinoAction.Load), CasinoAction.Load, 0),
            (nameof(CasinoAction.Return), CasinoAction.Return, 1),
            (nameof(CasinoAction.Play), CasinoAction.Play, 2),
            (nameof(CasinoAction.Credit), CasinoAction.Credit, 3),
        };
        foreach (var action in actions)
            Assert.AreEqual(action.Expected, Convert.ToInt32(action.Value), action.Name);

        var expectedVersion = new string(new[] { 'a', 'n', 'o', 'c', 'a', 's', 'i', 'n', 'o', '-', 'v', '1' });
        Assert.AreEqual(expectedVersion, CasinoRules.Version);
        CollectionAssert.AreEqual(
            new[] { "cherry", "lemon", "plum", "orange", "grapes", "watermelon", "bell", "seven" },
            CasinoRules.Symbols);
    }

    [TestMethod]
    public void LegacySlotPayoutVectorsRemainStable()
    {
        // Five equally split lines; payouts include each line's returned stake.
        Assert.AreEqual(1_200L, CasinoRules.EvaluateSlots(100, Enumerable.Repeat(0, 9).ToArray()).Payout);
        Assert.AreEqual(50_000L, CasinoRules.EvaluateSlots(50, Enumerable.Repeat(7, 9).ToArray()).Payout);

        // Two seven-lines (one horizontal and one diagonal) pay additively.
        Assert.AreEqual(40_000L, CasinoRules.EvaluateSlots(100, [7, 1, 2, 7, 7, 5, 7, 3, 7]).Payout);
    }

    [TestMethod]
    public void LegacyCoinflipAndRoulettePayoutVectorsRemainStable()
    {
        Assert.AreEqual(200L, CasinoRules.EvaluateNumber(new(CasinoGame.Coinflip, 100, 1), 1).Payout);
        Assert.AreEqual(0L, CasinoRules.EvaluateNumber(new(CasinoGame.Coinflip, 100, 1), 0).Payout);

        Assert.AreEqual(3_600L, CasinoRules.EvaluateNumber(new(CasinoGame.Roulette, 100, 17), 17).Payout);
        Assert.AreEqual(200L, CasinoRules.EvaluateNumber(new(CasinoGame.Roulette, 100, 37), 1).Payout);
        Assert.AreEqual(200L, CasinoRules.EvaluateNumber(new(CasinoGame.Roulette, 100, 38), 2).Payout);
        Assert.AreEqual(0L, CasinoRules.EvaluateNumber(new(CasinoGame.Roulette, 100, 39), 0).Payout);
        Assert.AreEqual(300L, CasinoRules.EvaluateNumber(new(CasinoGame.Roulette, 100, 43), 12).Payout);
        Assert.AreEqual(300L, CasinoRules.EvaluateNumber(new(CasinoGame.Roulette, 100, 46), 1).Payout);
    }

    [TestMethod]
    public void Migration027RequestAndReceiptJsonShapesStayCompatible()
    {
        // CasinoWallet stores this anonymous request with default System.Text.Json options.
        var request = new
        {
            player = 76561198000911555UL,
            action = CasinoAction.Play,
            amount = 0,
            game = new CasinoRequest(CasinoGame.Coinflip, 100, 1),
            rules = CasinoRules.Version,
        };

        Assert.AreEqual(
            """{"player":76561198000911555,"action":2,"amount":0,"game":{"Game":1,"Stake":100,"Choice":1},"rules":"anocasino-v1"}""",
            JsonSerializer.Serialize(request));

        var receipt = new CasinoReceipt(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new CasinoBalance(900, 100, 4),
            new CasinoOutcome(CasinoGame.Coinflip, [], 1, true, 200, "win"));

        Assert.AreEqual(
            """{"Id":"00000000-0000-0000-0000-000000000001","Balance":{"Available":900,"Allocated":100,"Revision":4},"Outcome":{"Game":1,"Symbols":[],"Number":1,"Won":true,"Payout":200,"Summary":"win"}}""",
            JsonSerializer.Serialize(receipt));
    }
}
