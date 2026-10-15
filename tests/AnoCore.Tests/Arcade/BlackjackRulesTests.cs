using System.Text.Json;
using AnoCore.Modules.Arcade.Casino;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AnoCore.Tests.Arcade;

[TestClass]
public sealed class BlackjackRulesTests
{
    private static BlackjackCard C(BlackjackSuit suit, BlackjackRank rank) => new(suit, rank);
    private static BlackjackCard[] Deck(params BlackjackCard[] drawOrder)
    {
        var fullDeck = Enum.GetValues<BlackjackSuit>()
            .SelectMany(suit => Enum.GetValues<BlackjackRank>()
                .Select(rank => new BlackjackCard(suit, rank)));
        return drawOrder.Concat(fullDeck.Except(drawOrder)).ToArray();
    }

    [TestMethod]
    public void HigherStartingStakeKeepsDoubleAndSettlementAccounting()
    {
        var deck = Deck(C(BlackjackSuit.Clubs, BlackjackRank.Ten), C(BlackjackSuit.Hearts, BlackjackRank.Six),
            C(BlackjackSuit.Spades, BlackjackRank.Five), C(BlackjackSuit.Diamonds, BlackjackRank.Nine), C(BlackjackSuit.Clubs, BlackjackRank.Two));
        var state = BlackjackRules.Start(10000, deck);
        Assert.IsTrue(BlackjackRules.LegalActions(state).Contains(BlackjackAction.Double));
        var doubled = BlackjackRules.Apply(state, state.Revision, BlackjackAction.Double);
        Assert.AreEqual(20000L, doubled.PlayerHands.Sum(hand => hand.Stake));
        Assert.AreEqual(20000L, doubled.PlayerHands[0].Stake);
    }

    [TestMethod]
    public void Start_is_deterministic_and_serializable()
    {
        var deck = Deck(C(BlackjackSuit.Clubs, BlackjackRank.Ten),
            C(BlackjackSuit.Hearts, BlackjackRank.Six),
            C(BlackjackSuit.Spades, BlackjackRank.Five),
            C(BlackjackSuit.Diamonds, BlackjackRank.Nine),
            C(BlackjackSuit.Clubs, BlackjackRank.Two));
        var state = BlackjackRules.Start(100, deck);
        var json = JsonSerializer.Serialize(state);
        var restored = JsonSerializer.Deserialize<BlackjackState>(json)!;

        Assert.AreEqual(json, JsonSerializer.Serialize(restored));
        Assert.IsTrue(BlackjackRules.LegalActions(restored).Contains(BlackjackAction.Stand));
        Assert.AreEqual(48, restored.RemainingDeck.Length);
    }

    [TestMethod]
    public void Natural_blackjack_pays_three_to_two_in_integer_units()
    {
        var state = BlackjackRules.Start(100,
            Deck(C(BlackjackSuit.Spades, BlackjackRank.Ace),
                C(BlackjackSuit.Clubs, BlackjackRank.Nine),
                C(BlackjackSuit.Hearts, BlackjackRank.King),
                C(BlackjackSuit.Diamonds, BlackjackRank.Seven)));

        Assert.AreEqual(BlackjackRoundStatus.Settled, state.Status);
        Assert.AreEqual(250L, state.TotalPayout);
        Assert.AreEqual(0, BlackjackRules.LegalActions(state).Count);
    }

    [TestMethod]
    public void Hit_consumes_one_card_and_aces_are_soft_then_hard()
    {
        var state = BlackjackRules.Start(100, Deck(
            C(BlackjackSuit.Clubs, BlackjackRank.Ace),
            C(BlackjackSuit.Hearts, BlackjackRank.Six),
            C(BlackjackSuit.Spades, BlackjackRank.Five),
            C(BlackjackSuit.Diamonds, BlackjackRank.Ten),
            C(BlackjackSuit.Clubs, BlackjackRank.Ten)));
        state = BlackjackRules.Apply(state, 0, BlackjackAction.Hit);

        Assert.AreEqual(16, state.PlayerHands[0].BestTotal);
        Assert.AreEqual(47, state.RemainingDeck.Length);
    }

    [TestMethod]
    public void Double_is_offered_only_on_initial_two_cards_and_stands_after_one_draw()
    {
        var state = BlackjackRules.Start(100, Deck(
            C(BlackjackSuit.Clubs, BlackjackRank.Five),
            C(BlackjackSuit.Hearts, BlackjackRank.Six),
            C(BlackjackSuit.Spades, BlackjackRank.Six),
            C(BlackjackSuit.Diamonds, BlackjackRank.Ten),
            C(BlackjackSuit.Clubs, BlackjackRank.Ten)));
        CollectionAssert.Contains(BlackjackRules.LegalActions(state).ToArray(), BlackjackAction.Double);

        state = BlackjackRules.Apply(state, 0, BlackjackAction.Double);
        Assert.AreEqual(200L, state.PlayerHands[0].Stake);
        Assert.IsTrue(state.PlayerHands[0].IsStanding);
    }

    [TestMethod]
    public void Split_aces_receive_one_card_each_then_stand()
    {
        var state = BlackjackRules.Start(100, Deck(
            C(BlackjackSuit.Clubs, BlackjackRank.Ace),
            C(BlackjackSuit.Hearts, BlackjackRank.Six),
            C(BlackjackSuit.Spades, BlackjackRank.Ace),
            C(BlackjackSuit.Diamonds, BlackjackRank.Ten),
            C(BlackjackSuit.Clubs, BlackjackRank.King),
            C(BlackjackSuit.Hearts, BlackjackRank.Queen),
            C(BlackjackSuit.Spades, BlackjackRank.Two)));
        state = BlackjackRules.Apply(state, 0, BlackjackAction.Split);

        Assert.AreEqual(2, state.PlayerHands.Length);
        Assert.AreEqual(2, state.PlayerHands[0].Cards.Length);
        Assert.AreEqual(2, state.PlayerHands[1].Cards.Length);
        Assert.IsTrue(state.PlayerHands.All(hand => hand.IsStanding));
        Assert.AreEqual(45, state.RemainingDeck.Length);
    }

    [TestMethod]
    public void Split_is_limited_to_two_hands_and_only_equal_ranks()
    {
        var state = BlackjackRules.Start(100, Deck(
            C(BlackjackSuit.Clubs, BlackjackRank.Eight),
            C(BlackjackSuit.Hearts, BlackjackRank.Six),
            C(BlackjackSuit.Spades, BlackjackRank.Eight),
            C(BlackjackSuit.Diamonds, BlackjackRank.Ten),
            C(BlackjackSuit.Clubs, BlackjackRank.Two),
            C(BlackjackSuit.Hearts, BlackjackRank.Three)));
        state = BlackjackRules.Apply(state, 0, BlackjackAction.Split);

        Assert.AreEqual(2, state.PlayerHands.Length);
        Assert.IsFalse(BlackjackRules.LegalActions(state).Contains(BlackjackAction.Split));
    }

    [TestMethod]
    public void Dealer_stands_on_soft_seventeen()
    {
        var state = BlackjackRules.Start(100, Deck(
            C(BlackjackSuit.Clubs, BlackjackRank.Ten),
            C(BlackjackSuit.Hearts, BlackjackRank.Ace),
            C(BlackjackSuit.Spades, BlackjackRank.Seven),
            C(BlackjackSuit.Diamonds, BlackjackRank.Six),
            C(BlackjackSuit.Clubs, BlackjackRank.King)));
        state = BlackjackRules.Apply(state, 0, BlackjackAction.Stand);

        Assert.AreEqual(2, state.DealerCards.Length);
        Assert.AreEqual(100L, state.TotalPayout);
        Assert.AreEqual(48, state.RemainingDeck.Length);
    }

    [TestMethod]
    public void Dealer_hits_soft_sixteen()
    {
        var state = BlackjackRules.Start(100, Deck(
            C(BlackjackSuit.Clubs, BlackjackRank.Ten),
            C(BlackjackSuit.Hearts, BlackjackRank.Ace),
            C(BlackjackSuit.Spades, BlackjackRank.Six),
            C(BlackjackSuit.Diamonds, BlackjackRank.Five),
            C(BlackjackSuit.Clubs, BlackjackRank.Two)));
        state = BlackjackRules.Apply(state, 0, BlackjackAction.Stand);

        Assert.AreEqual(3, state.DealerCards.Length);
        Assert.AreEqual(18, new BlackjackHand(state.DealerCards, 100, false, false, false).BestTotal);
        Assert.AreEqual(47, state.RemainingDeck.Length);
    }

    [TestMethod]
    public void Version_revision_and_physical_card_uniqueness_are_validated()
    {
        var state = BlackjackRules.Start(100, Deck(
            C(BlackjackSuit.Clubs, BlackjackRank.Ten),
            C(BlackjackSuit.Hearts, BlackjackRank.Six),
            C(BlackjackSuit.Spades, BlackjackRank.Five),
            C(BlackjackSuit.Diamonds, BlackjackRank.Nine),
            C(BlackjackSuit.Clubs, BlackjackRank.Two)));

        Assert.ThrowsExactly<InvalidOperationException>(() => BlackjackRules.Apply(state, 1, BlackjackAction.Hit));
        Assert.ThrowsExactly<ArgumentException>(() => BlackjackRules.LegalActions(state with { RulesVersion = "old" }));
        Assert.ThrowsExactly<ArgumentException>(() => BlackjackRules.LegalActions(state with
        {
            RemainingDeck = state.RemainingDeck.SetItem(0, state.DealerCards[0]),
        }));

        var advanced = BlackjackRules.Apply(state, 0, BlackjackAction.Stand);
        Assert.AreEqual(1L, advanced.Revision);
        Assert.AreEqual(BlackjackRules.RulesVersion, advanced.RulesVersion);
    }

    [TestMethod]
    public void Invalid_stakes_and_exhausted_deck_are_rejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BlackjackRules.Start(51,
            Deck(C(BlackjackSuit.Clubs, BlackjackRank.Two), C(BlackjackSuit.Hearts, BlackjackRank.Two),
                C(BlackjackSuit.Spades, BlackjackRank.Three), C(BlackjackSuit.Diamonds, BlackjackRank.Three))));
        Assert.ThrowsExactly<ArgumentException>(() => BlackjackRules.Start(100,
            Deck(C(BlackjackSuit.Clubs, BlackjackRank.Two), C(BlackjackSuit.Hearts, BlackjackRank.Two),
                C(BlackjackSuit.Spades, BlackjackRank.Three)).Take(3)));
    }
}
