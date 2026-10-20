using System.Text.Json;
using AnoCore.Modules.Arcade.Casino;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class BlackjackExtendedRulesTests
{
    internal static BlackjackCard[] Deck(params BlackjackCard[] first) => first.Concat(
        Enum.GetValues<BlackjackSuit>().SelectMany(suit => Enum.GetValues<BlackjackRank>()
            .Select(rank => new BlackjackCard(suit, rank))).Except(first)).ToArray();
    private static BlackjackCard C(BlackjackSuit suit, BlackjackRank rank) => new(suit, rank);
    internal static BlackjackState InsuranceRound(bool dealerNatural, bool playerNatural = false) => BlackjackRules.Start(100, Deck(
        new(BlackjackSuit.Clubs, playerNatural ? BlackjackRank.Ace : BlackjackRank.Ten),
        new(BlackjackSuit.Hearts, BlackjackRank.Ace),
        new(BlackjackSuit.Spades, playerNatural ? BlackjackRank.King : BlackjackRank.Nine),
        new(BlackjackSuit.Diamonds, dealerNatural ? BlackjackRank.King : BlackjackRank.Nine)));

    [TestMethod]
    public void SeededCompleteRoundsPreserveCardsAndStayInsideReservedExposure()
    {
        var random = new Random(526);
        for (var round = 0; round < 500; round++)
        {
            var deck = Deck();
            random.Shuffle(deck);
            var state = BlackjackRules.Start(100, deck);
            var actions = 0;
            while (state.Status != BlackjackRoundStatus.Settled)
            {
                Assert.IsTrue(actions++ < 80);
                var legal = BlackjackRules.LegalActions(state);
                Assert.IsTrue(legal.Count > 0);
                state = BlackjackRules.Apply(state, state.Revision, legal[random.Next(legal.Count)]);
                Assert.AreEqual(52, state.PlayerHands.Sum(hand => hand.Cards.Length) + state.DealerCards.Length + state.RemainingDeck.Length);
            }
            Assert.IsTrue(BlackjackRules.TotalStake(state) <= 850);
            Assert.IsTrue(state.TotalPayout <= 1700);
            Assert.AreEqual(0, BlackjackRules.LegalActions(state).Count);
        }
    }

    [TestMethod]
    [DataRow(false, false, 0L)]
    [DataRow(true, false, 150L)]
    [DataRow(false, true, 250L)]
    [DataRow(true, true, 250L)]
    public void InsuranceHasSeparateExactStakeAndOnlyPaysOnDealerNatural(bool dealerNatural, bool playerNatural, long expected)
    {
        var state = InsuranceRound(dealerNatural, playerNatural);
        Assert.AreEqual(BlackjackRoundStatus.InsuranceOffer, state.Status);
        Assert.IsFalse(BlackjackRules.LegalActions(state).Contains(BlackjackAction.Hit));
        Assert.ThrowsExactly<InvalidOperationException>(() => BlackjackRules.Apply(state, 0, BlackjackAction.Stand));
        state = JsonSerializer.Deserialize<BlackjackState>(JsonSerializer.Serialize(state))!;
        state = BlackjackRules.Apply(state, state.Revision, BlackjackAction.Insurance);
        Assert.AreEqual(150L, BlackjackRules.TotalStake(state));
        Assert.AreEqual(dealerNatural ? 150L : 0L, state.InsurancePayout);
        if (state.Status != BlackjackRoundStatus.Settled) state = BlackjackRules.Apply(state, state.Revision, BlackjackAction.Stand);
        Assert.AreEqual(expected, state.TotalPayout);
        Assert.ThrowsExactly<InvalidOperationException>(() => BlackjackRules.Apply(state, state.Revision, BlackjackAction.Insurance));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EvenMoneyReturnsExactlyTwoStakesWithoutInsuranceDebit(bool dealerNatural)
    {
        var state = InsuranceRound(dealerNatural, true);
        state = BlackjackRules.Apply(state, 0, BlackjackAction.EvenMoney);
        Assert.AreEqual(BlackjackRoundStatus.Settled, state.Status);
        Assert.AreEqual(200L, state.TotalPayout);
        Assert.AreEqual(100L, BlackjackRules.TotalStake(state));
        Assert.AreEqual(0L, state.InsuranceStake);
        Assert.IsTrue(state.EvenMoneyTaken);
        Assert.IsFalse(BlackjackRules.LegalActions(InsuranceRound(false)).Contains(BlackjackAction.EvenMoney));
    }

    [TestMethod]
    public void DeclineDoesNotChargeAndSurrenderRefundsHalfOnlyBeforeTakingCards()
    {
        var state = InsuranceRound(false);
        state = BlackjackRules.Apply(state, 0, BlackjackAction.DeclineInsurance);
        Assert.AreEqual(100L, BlackjackRules.TotalStake(state));
        var hit = BlackjackRules.Apply(state, state.Revision, BlackjackAction.Hit);
        Assert.IsFalse(BlackjackRules.LegalActions(hit).Contains(BlackjackAction.Surrender));
        state = BlackjackRules.Apply(state, state.Revision, BlackjackAction.Surrender);
        Assert.AreEqual(50L, state.TotalPayout);
        Assert.IsTrue(state.PlayerHands[0].IsSurrendered);
        Assert.AreEqual(2, state.DealerCards.Length);
    }

    [TestMethod]
    public void ResplitSupportsFourHandsAndRetainsLeftToRightTurnOrder()
    {
        var state = BlackjackRules.Start(100, Deck(
            C(BlackjackSuit.Clubs, BlackjackRank.Eight), C(BlackjackSuit.Hearts, BlackjackRank.Six),
            C(BlackjackSuit.Spades, BlackjackRank.Eight), C(BlackjackSuit.Diamonds, BlackjackRank.Ten),
            C(BlackjackSuit.Hearts, BlackjackRank.Eight), C(BlackjackSuit.Diamonds, BlackjackRank.Eight),
            C(BlackjackSuit.Clubs, BlackjackRank.Two), C(BlackjackSuit.Clubs, BlackjackRank.Three),
            C(BlackjackSuit.Clubs, BlackjackRank.Four), C(BlackjackSuit.Clubs, BlackjackRank.Five)));
        state = BlackjackRules.Apply(state, 0, BlackjackAction.Split);
        state = BlackjackRules.Apply(state, 1, BlackjackAction.Split);
        Assert.AreEqual(3, state.PlayerHands.Length);
        Assert.AreEqual(0, state.ActiveHandIndex);
        Assert.IsFalse(BlackjackRules.LegalActions(state).Contains(BlackjackAction.Surrender));
        state = BlackjackRules.Apply(state, 2, BlackjackAction.Stand);
        state = BlackjackRules.Apply(state, 3, BlackjackAction.Stand);
        Assert.AreEqual(2, state.ActiveHandIndex);
        state = BlackjackRules.Apply(state, 4, BlackjackAction.Split);
        Assert.AreEqual(4, state.PlayerHands.Length);
        Assert.IsFalse(BlackjackRules.LegalActions(state).Contains(BlackjackAction.Split));
        Assert.AreEqual(400L, BlackjackRules.TotalStake(state));
    }

    [TestMethod]
    public void LegacyRoundsRetainImmediatePeekAndDoNotGainNewActions()
    {
        var deck = Deck(C(BlackjackSuit.Clubs, BlackjackRank.Eight), C(BlackjackSuit.Hearts, BlackjackRank.Ace),
            C(BlackjackSuit.Spades, BlackjackRank.Eight), C(BlackjackSuit.Diamonds, BlackjackRank.Six));
        var state = BlackjackRules.Start(100, deck, BlackjackRules.LegacyVersion);
        Assert.AreEqual(BlackjackRoundStatus.PlayerTurn, state.Status);
        Assert.IsFalse(BlackjackRules.LegalActions(state).Contains(BlackjackAction.Surrender));
        state = BlackjackRules.Apply(state, 0, BlackjackAction.Split);
        Assert.AreEqual(BlackjackRules.LegacyVersion, state.RulesVersion);
        Assert.AreEqual(2, state.PlayerHands.Length);
    }
}
