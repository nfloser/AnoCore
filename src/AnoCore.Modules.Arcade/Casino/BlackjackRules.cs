using System.Collections.Immutable;

namespace AnoCore.Modules.Arcade.Casino;

public enum BlackjackSuit { Clubs, Diamonds, Hearts, Spades }
public enum BlackjackRank { Two = 2, Three, Four, Five, Six, Seven, Eight, Nine, Ten, Jack, Queen, King, Ace }
public enum BlackjackAction { Hit, Stand, Double, Split }
public enum BlackjackRoundStatus { PlayerTurn, Settled }

public readonly record struct BlackjackCard(BlackjackSuit Suit, BlackjackRank Rank)
{
    public int Value => Rank switch
    {
        BlackjackRank.Ace => 11,
        BlackjackRank.King or BlackjackRank.Queen or BlackjackRank.Jack => 10,
        _ => (int)Rank,
    };
}

public sealed record BlackjackHand(
    ImmutableArray<BlackjackCard> Cards,
    long Stake,
    bool IsStanding,
    bool IsSplitAces,
    bool IsSplit)
{
    public int BestTotal
    {
        get
        {
            var total = Cards.Sum(card => card.Value);
            var aces = Cards.Count(card => card.Rank == BlackjackRank.Ace);
            while (total > 21 && aces-- > 0) total -= 10;
            return total;
        }
    }

    public bool IsSoft
    {
        get
        {
            var aceCount = Cards.Count(card => card.Rank == BlackjackRank.Ace);
            var total = Cards.Sum(card => card.Value);
            return aceCount > 0 && total - 10 * (aceCount - 1) <= 21;
        }
    }

    public bool IsBust => BestTotal > 21;
}

public sealed record BlackjackState(
    long InitialStake,
    ImmutableArray<BlackjackHand> PlayerHands,
    ImmutableArray<BlackjackCard> DealerCards,
    ImmutableArray<BlackjackCard> RemainingDeck,
    int ActiveHandIndex,
    BlackjackRoundStatus Status,
    long TotalPayout);

/// <summary>
/// Pure deterministic Blackjack rules. The caller supplies a cryptographically shuffled deck
/// in draw order; this type neither persists state nor generates randomness.
/// </summary>
public static class BlackjackRules
{
    public const long MinimumStake = 50;
    public const long MaximumStake = 500;
    public const int MaximumHands = 2;

    public static BlackjackState Start(long stake, IEnumerable<BlackjackCard> shuffledDeck)
    {
        ValidateStake(stake);
        ArgumentNullException.ThrowIfNull(shuffledDeck);
        var deck = shuffledDeck.ToImmutableArray();
        ValidateDeck(deck);
        if (deck.Length < 4) throw new ArgumentException("A round requires at least four cards.", nameof(shuffledDeck));

        var remaining = deck;
        var player = ImmutableArray.CreateBuilder<BlackjackCard>(2);
        var dealer = ImmutableArray.CreateBuilder<BlackjackCard>(2);
        (var first, remaining) = Draw(remaining);
        player.Add(first);
        (var dealerUp, remaining) = Draw(remaining);
        dealer.Add(dealerUp);
        (var second, remaining) = Draw(remaining);
        player.Add(second);
        (var dealerHole, remaining) = Draw(remaining);
        dealer.Add(dealerHole);

        var hand = new BlackjackHand(player.MoveToImmutable(), stake, false, false, false);
        var state = new BlackjackState(stake, [hand], dealer.MoveToImmutable(), remaining, 0,
            BlackjackRoundStatus.PlayerTurn, 0);

        var playerNatural = IsNatural(hand);
        var dealerNatural = IsNatural(new BlackjackHand(state.DealerCards, stake, false, false, false));
        if (playerNatural || dealerNatural)
            return Settle(state with { Status = BlackjackRoundStatus.Settled }, dealerAlreadyFinal: true);

        return state;
    }

    public static IReadOnlyList<BlackjackAction> LegalActions(BlackjackState state)
    {
        ValidateState(state);
        if (state.Status != BlackjackRoundStatus.PlayerTurn) return Array.Empty<BlackjackAction>();

        var hand = state.PlayerHands[state.ActiveHandIndex];
        if (hand.IsStanding || hand.IsBust) return Array.Empty<BlackjackAction>();

        var actions = new List<BlackjackAction>(4);
        if (hand.BestTotal < 21) actions.Add(BlackjackAction.Hit);
        actions.Add(BlackjackAction.Stand);
        if (hand.Cards.Length == 2 && hand.Stake <= MaximumStake)
            actions.Add(BlackjackAction.Double);
        if (state.PlayerHands.Length < MaximumHands && hand.Cards.Length == 2
            && hand.Cards[0].Rank == hand.Cards[1].Rank)
            actions.Add(BlackjackAction.Split);
        return actions;
    }

    public static BlackjackState Apply(BlackjackState state, BlackjackAction action)
    {
        ValidateState(state);
        if (!LegalActions(state).Contains(action))
            throw new InvalidOperationException($"Action {action} is not legal in the current state.");

        var hands = state.PlayerHands;
        var hand = hands[state.ActiveHandIndex];
        var deck = state.RemainingDeck;

        switch (action)
        {
            case BlackjackAction.Hit:
            {
                (var card, deck) = Draw(deck);
                hand = hand with { Cards = hand.Cards.Add(card) };
                if (hand.IsBust || hand.BestTotal == 21) hand = hand with { IsStanding = true };
                hands = hands.SetItem(state.ActiveHandIndex, hand);
                return ContinueOrSettle(state with { PlayerHands = hands, RemainingDeck = deck });
            }
            case BlackjackAction.Stand:
                hands = hands.SetItem(state.ActiveHandIndex, hand with { IsStanding = true });
                return ContinueOrSettle(state with { PlayerHands = hands });
            case BlackjackAction.Double:
            {
                (var card, deck) = Draw(deck);
                hand = hand with
                {
                    Cards = hand.Cards.Add(card),
                    Stake = checked(hand.Stake * 2),
                    IsStanding = true,
                };
                hands = hands.SetItem(state.ActiveHandIndex, hand);
                return ContinueOrSettle(state with { PlayerHands = hands, RemainingDeck = deck });
            }
            case BlackjackAction.Split:
            {
                (var firstCard, deck) = Draw(deck);
                (var secondCard, deck) = Draw(deck);
                var splitAces = hand.Cards[0].Rank == BlackjackRank.Ace;
                var left = hand with
                {
                    Cards = ImmutableArray.Create(hand.Cards[0], firstCard),
                    IsStanding = splitAces || hand.Cards[0].Value + firstCard.Value == 21,
                    IsSplitAces = splitAces,
                    IsSplit = true,
                };
                var right = hand with
                {
                    Cards = ImmutableArray.Create(hand.Cards[1], secondCard),
                    IsStanding = splitAces || hand.Cards[1].Value + secondCard.Value == 21,
                    IsSplitAces = splitAces,
                    IsSplit = true,
                };
                hands = hands.SetItem(state.ActiveHandIndex, left)
                    .Insert(state.ActiveHandIndex + 1, right);
                var next = state with { PlayerHands = hands, RemainingDeck = deck };
                return AllHandsDone(hands) ? Settle(next) : AdvanceToActionableHand(next, state.ActiveHandIndex);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private static BlackjackState ContinueOrSettle(BlackjackState state)
    {
        if (!AllHandsDone(state.PlayerHands))
            return AdvanceToActionableHand(state, state.ActiveHandIndex);
        return Settle(state);
    }

    private static BlackjackState AdvanceToActionableHand(BlackjackState state, int afterIndex)
    {
        for (var i = afterIndex + 1; i < state.PlayerHands.Length; i++)
            if (!state.PlayerHands[i].IsStanding && !state.PlayerHands[i].IsBust)
                return state with { ActiveHandIndex = i };
        for (var i = 0; i < state.PlayerHands.Length; i++)
            if (!state.PlayerHands[i].IsStanding && !state.PlayerHands[i].IsBust)
                return state with { ActiveHandIndex = i };
        return Settle(state);
    }

    private static BlackjackState Settle(BlackjackState state, bool dealerAlreadyFinal = false)
    {
        var deck = state.RemainingDeck;
        var dealer = state.DealerCards;
        var dealerHand = new BlackjackHand(dealer, state.InitialStake, false, false, false);
        if (!dealerAlreadyFinal && state.PlayerHands.Any(hand => !hand.IsBust))
        {
            while (dealerHand.BestTotal < 17 || dealerHand.BestTotal == 17 && dealerHand.IsSoft)
            {
                (var card, deck) = Draw(deck);
                dealer = dealer.Add(card);
                dealerHand = dealerHand with { Cards = dealer };
            }
        }

        long payout = 0;
        foreach (var hand in state.PlayerHands)
        {
            if (hand.IsBust) continue;
            var total = hand.BestTotal;
            var dealerTotal = dealerHand.BestTotal;
            if (IsNatural(hand) && !IsNatural(dealerHand))
                payout = checked(payout + checked(hand.Stake * 5 / 2));
            else if (IsNatural(dealerHand) && !IsNatural(hand))
                continue;
            else if (total > dealerTotal || dealerHand.IsBust)
                payout = checked(payout + checked(hand.Stake * 2));
            else if (total == dealerTotal)
                payout = checked(payout + hand.Stake);
        }

        return state with
        {
            DealerCards = dealer,
            RemainingDeck = deck,
            Status = BlackjackRoundStatus.Settled,
            TotalPayout = payout,
            ActiveHandIndex = Math.Min(state.ActiveHandIndex, state.PlayerHands.Length - 1),
        };
    }

    private static bool AllHandsDone(ImmutableArray<BlackjackHand> hands) =>
        hands.All(hand => hand.IsStanding || hand.IsBust);

    private static bool IsNatural(BlackjackHand hand) =>
        !hand.IsSplit && hand.Cards.Length == 2 && hand.BestTotal == 21;

    private static (BlackjackCard Card, ImmutableArray<BlackjackCard> Remaining) Draw(
        ImmutableArray<BlackjackCard> deck)
    {
        if (deck.IsDefaultOrEmpty) throw new ArgumentException("The supplied deck is exhausted.", nameof(deck));
        return (deck[0], deck.RemoveAt(0));
    }

    private static void ValidateStake(long stake)
    {
        if (stake is < MinimumStake or > MaximumStake || stake % 50 != 0)
            throw new ArgumentOutOfRangeException(nameof(stake), "Stake must be 50–500 in 50-unit increments.");
    }

    private static void ValidateDeck(ImmutableArray<BlackjackCard> deck)
    {
        if (deck.IsDefault || deck.Length > 52)
            throw new ArgumentException("Deck must contain between 4 and 52 cards.", nameof(deck));
        if (deck.Any(card => !Enum.IsDefined(card.Suit) || !Enum.IsDefined(card.Rank)))
            throw new ArgumentException("Deck contains an invalid card.", nameof(deck));
        if (deck.Distinct().Count() != deck.Length)
            throw new ArgumentException("Deck cannot contain duplicate physical cards.", nameof(deck));
    }

    private static void ValidateState(BlackjackState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateStake(state.InitialStake);
        if (state.PlayerHands.IsDefaultOrEmpty || state.PlayerHands.Length > MaximumHands
            || state.DealerCards.IsDefault || state.DealerCards.Length < 2
            || state.RemainingDeck.IsDefault || state.ActiveHandIndex < 0
            || state.ActiveHandIndex >= state.PlayerHands.Length || state.TotalPayout < 0
            || !Enum.IsDefined(state.Status))
            throw new ArgumentException("Blackjack state is invalid.", nameof(state));
        if (state.PlayerHands.Any(hand => hand.Cards.IsDefaultOrEmpty || hand.Cards.Length > 21
            || hand.Stake < state.InitialStake || hand.Stake > state.InitialStake * 2
            || hand.Stake % state.InitialStake != 0))
            throw new ArgumentException("Blackjack state contains an invalid hand.", nameof(state));
    }
}
