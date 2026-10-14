using System.Security.Cryptography;
using System.Text.Json;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Arcade.Casino;

public enum CasinoRoundGame { Blackjack, Risk, Book }
public enum CasinoRoundAction { Start, Hit, Stand, Double, Split, Risk, Bank, FreeSpin }
public sealed record DurableCasinoRoundOperation(Guid Id, PlayerId Player, Guid SessionId,
    CasinoRoundGame Game, CasinoRoundAction Action, int Stake, long ExpectedRevision);
public sealed record DurableCasinoRoundSnapshot(Guid Id, PlayerId Player, CasinoRoundGame Game,
    int Stake, long Revision, bool IsComplete, long TotalStake, long Payout, string StateJson);
public sealed record DurableCasinoRoundReceipt(Guid Id, CasinoBalance Balance, DurableCasinoRoundSnapshot Round);

public interface ICasinoRounds
{
    ValueTask<DurableCasinoRoundSnapshot?> ReadAsync(PlayerId player, CancellationToken token = default);
    ValueTask<DurableCasinoRoundReceipt> ApplyAsync(DurableCasinoRoundOperation operation, CancellationToken token = default);
}

/// <summary>Adapts versioned native rules to account-serialized durable rounds.</summary>
public sealed class CasinoRoundService(ICasinoRoundStore store) : ICasinoRounds
{
    public async ValueTask<DurableCasinoRoundSnapshot?> ReadAsync(PlayerId player, CancellationToken token = default)
    {
        var round = await store.ReadActiveAsync(player, token).ConfigureAwait(false);
        return round is null ? null : View(round);
    }

    public async ValueTask<DurableCasinoRoundReceipt> ApplyAsync(DurableCasinoRoundOperation operation, CancellationToken token = default)
    {
        if (operation.Id == Guid.Empty || operation.SessionId == Guid.Empty || operation.Player is null
            || !Enum.IsDefined(operation.Game) || !Enum.IsDefined(operation.Action)
            || operation.Stake is < 50 or > 500 || operation.Stake % 50 != 0 || operation.ExpectedRevision < 0
            || (operation.Action == CasinoRoundAction.Start) != (operation.ExpectedRevision == 0))
            throw new CasinoRejectedException("Ungültige Spielaktion.");
        var kind = operation.Game switch
        {
            CasinoRoundGame.Blackjack => CasinoRoundKind.Blackjack,
            CasinoRoundGame.Risk => CasinoRoundKind.Risk,
            _ => CasinoRoundKind.BonusSlot,
        };
        var request = new CasinoRoundOperation(operation.Id, operation.SessionId, operation.Player,
            kind, operation.ExpectedRevision, JsonSerializer.Serialize(new { operation.Action, operation.Stake }));
        var receipt = await store.ApplyAsync(request, current => Plan(operation, current),
            current => Transition(operation, current), token).ConfigureAwait(false);
        return new(receipt.OperationId, receipt.Balance, View(receipt.Round, operation.Stake));
    }

    private static CasinoRoundPlan Plan(DurableCasinoRoundOperation op, CasinoRoundSnapshot? current)
    {
        if (current is null) return new(op.Stake, Exposure(op.Game, op.Stake));
        var view = View(current);
        if (view.Stake != op.Stake || current.RulesVersion != Version(op.Game))
            throw new CasinoRejectedException("Einsatz oder gespeicherte Spielregeln stimmen nicht überein.");
        long debit = 0;
        switch (op.Game)
        {
            case CasinoRoundGame.Blackjack:
                var blackjack = Read<BlackjackState>(current);
                var action = BlackjackActionFor(op.Action);
                if (!BlackjackRules.LegalActions(blackjack).Contains(action))
                    throw new CasinoRejectedException("Diese Blackjack-Aktion ist nicht möglich.");
                if (action is BlackjackAction.Double or BlackjackAction.Split)
                    debit = blackjack.PlayerHands[blackjack.ActiveHandIndex].Stake;
                break;
            case CasinoRoundGame.Risk:
                var risk = Read<RiskRoundState>(current);
                if (op.Action is not (CasinoRoundAction.Bank or CasinoRoundAction.Risk)
                    || risk.Status != RiskRoundStatus.Active
                    || op.Action == CasinoRoundAction.Risk && (risk.Gambles >= RiskRules.MaximumGambles
                        || risk.HeldPayout > RiskRules.MaximumHeldExposure / 2))
                    throw new CasinoRejectedException("Diese Risk-Aktion ist nicht möglich.");
                break;
            case CasinoRoundGame.Book:
                if (op.Action != CasinoRoundAction.FreeSpin || Read<BonusSlotSpinResult>(current).State.FreeSpinsRemaining <= 0)
                    throw new CasinoRejectedException("Keine Freispiele verfügbar.");
                break;
        }
        return new(debit, Exposure(op.Game, op.Stake));
    }

    private static CasinoRoundMutation Transition(DurableCasinoRoundOperation op, CasinoRoundSnapshot? current)
    {
        object state;
        bool complete;
        long payout;
        long debit;
        switch (op.Game)
        {
            case CasinoRoundGame.Blackjack:
                var before = current is null ? null : Read<BlackjackState>(current);
                var blackjack = before is null ? BlackjackRules.Start(op.Stake, Shuffle())
                    : BlackjackRules.Apply(before, before.Revision, BlackjackActionFor(op.Action));
                state = blackjack;
                complete = blackjack.Status == BlackjackRoundStatus.Settled;
                payout = blackjack.TotalPayout;
                debit = blackjack.PlayerHands.Sum(hand => hand.Stake) - (before?.PlayerHands.Sum(hand => hand.Stake) ?? 0);
                break;
            case CasinoRoundGame.Risk:
                var previous = current is null ? null : Read<RiskRoundState>(current);
                var risk = previous is null ? RiskRules.Start(op.Stake)
                    : op.Action == CasinoRoundAction.Bank ? RiskRules.Bank(previous, previous.Revision)
                    : RiskRules.Double(previous, previous.Revision, RandomNumberGenerator.GetInt32(2) == 1);
                state = risk;
                complete = risk.Status != RiskRoundStatus.Active;
                payout = risk.FinalPayout ?? 0;
                debit = previous is null ? op.Stake : 0;
                break;
            default:
                var bonus = current is null ? BonusSlotRules.StartRound(op.Stake, RandomNumberGenerator.GetInt32)
                    : BonusSlotRules.PlayFreeSpin(Read<BonusSlotSpinResult>(current).State, RandomNumberGenerator.GetInt32);
                state = bonus;
                complete = bonus.State.FreeSpinsRemaining == 0;
                payout = bonus.State.TotalPayout;
                debit = bonus.ChargedStake;
                break;
        }
        var json = JsonSerializer.Serialize(state, state.GetType());
        return new(Version(op.Game), json, debit, complete ? payout : 0,
            complete ? 0 : Exposure(op.Game, op.Stake), complete, "{}");
    }

    private static DurableCasinoRoundSnapshot View(CasinoRoundSnapshot round, int fallbackStake = 0)
    {
        CasinoRoundGame game;
        int stake;
        long totalStake;
        long payout;
        switch (round.Kind)
        {
            case CasinoRoundKind.Blackjack:
                var blackjack = Read<BlackjackState>(round);
                game = CasinoRoundGame.Blackjack;
                stake = checked((int)blackjack.InitialStake);
                totalStake = blackjack.PlayerHands.Sum(hand => hand.Stake);
                payout = blackjack.TotalPayout;
                break;
            case CasinoRoundKind.Risk:
                var risk = Read<RiskRoundState>(round);
                game = CasinoRoundGame.Risk;
                stake = risk.HeldPayout == 0 ? fallbackStake : checked((int)(risk.HeldPayout / (1L << risk.Gambles)));
                totalStake = stake;
                payout = risk.FinalPayout ?? 0;
                break;
            default:
                var bonus = Read<BonusSlotSpinResult>(round);
                game = CasinoRoundGame.Book;
                stake = bonus.State.LockedStake;
                totalStake = stake;
                payout = bonus.State.TotalPayout;
                break;
        }
        return new(round.RoundId, round.Player, game, stake, round.Revision,
            round.Status == CasinoRoundStatus.Completed, totalStake, payout, round.StateJson);
    }

    private static T Read<T>(CasinoRoundSnapshot round) => JsonSerializer.Deserialize<T>(round.StateJson)
        ?? throw new CasinoRejectedException("Gespeicherter Spielstand ist ungültig.");
    private static string Version(CasinoRoundGame game) => game switch
    {
        CasinoRoundGame.Blackjack => BlackjackRules.RulesVersion,
        CasinoRoundGame.Risk => RiskRules.Version,
        _ => BonusSlotRules.Version,
    };
    private static long Exposure(CasinoRoundGame game, int stake) => game switch
    {
        CasinoRoundGame.Blackjack => stake * 8L,
        CasinoRoundGame.Risk => stake * (1L << RiskRules.MaximumGambles),
        _ => BonusSlotRules.MaximumRoundExposure(stake),
    };
    private static BlackjackAction BlackjackActionFor(CasinoRoundAction action) => action switch
    {
        CasinoRoundAction.Hit => BlackjackAction.Hit,
        CasinoRoundAction.Stand => BlackjackAction.Stand,
        CasinoRoundAction.Double => BlackjackAction.Double,
        CasinoRoundAction.Split => BlackjackAction.Split,
        _ => throw new CasinoRejectedException("Ungültige Blackjack-Aktion."),
    };
    private static BlackjackCard[] Shuffle()
    {
        var deck = Enum.GetValues<BlackjackSuit>().SelectMany(suit =>
            Enum.GetValues<BlackjackRank>().Select(rank => new BlackjackCard(suit, rank))).ToArray();
        RandomNumberGenerator.Shuffle(deck.AsSpan());
        return deck;
    }
}
