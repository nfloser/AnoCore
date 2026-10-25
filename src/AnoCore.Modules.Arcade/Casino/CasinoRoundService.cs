using System.Security.Cryptography;
using System.Text.Json;
using AnoCore.Abstractions.Players;

namespace AnoCore.Modules.Arcade.Casino;

public enum CasinoRoundGame { Blackjack, Risk, Book, BiggestBoy, Spitze }
public enum CasinoRoundAction { Start, Hit, Stand, Double, Split, Risk, Bank, FreeSpin, SelectSymbol, Insurance, DeclineInsurance, Surrender, EvenMoney, Spin, Collect, ActionGame }
public sealed record DurableCasinoRoundOperation(Guid Id, PlayerId Player, Guid SessionId,
    CasinoRoundGame Game, CasinoRoundAction Action, int Stake, long ExpectedRevision, int Choice = -1);
public sealed record DurableCasinoRoundSnapshot(Guid Id, PlayerId Player, CasinoRoundGame Game,
    int Stake, long Revision, bool IsComplete, long TotalStake, long Payout, string StateJson);
public sealed record DurableCasinoRoundReceipt(Guid Id, CasinoBalance Balance, DurableCasinoRoundSnapshot Round);

public interface ICasinoRounds
{
    ValueTask<DurableCasinoRoundSnapshot?> ReadAsync(PlayerId player, CancellationToken token = default);
    ValueTask<DurableCasinoRoundReceipt> ApplyAsync(DurableCasinoRoundOperation operation, CancellationToken token = default);
}

/// <summary>Adapts versioned native rules to account-serialized durable rounds.</summary>
public sealed class CasinoRoundService(ICasinoRoundStore store, int biggestBoyMaximumStake = BiggestBoyRules.MaximumStake,
    string biggestBoyProfile = BiggestBoyProfiles.Standard, CasinoStakeLimits? stakeLimits = null, SlotOutcomeSettings? biggestBoyOutcomes = null,
    SlotOutcomeSettings? bookOutcomes = null) : ICasinoRounds
{
    private readonly CasinoStakeLimits _stakeLimits = (stakeLimits ?? new()).Validate();
    private readonly SlotOutcomeSettings? _biggestBoyOutcomes = biggestBoyOutcomes?.Validate();
    private readonly SlotOutcomeSettings? _bookOutcomes = bookOutcomes?.Validate();
    private readonly string _biggestBoyProfile = BiggestBoyProfiles.Validate(biggestBoyProfile);

    public async ValueTask<DurableCasinoRoundSnapshot?> ReadAsync(PlayerId player, CancellationToken token = default)
    {
        var round = await store.ReadActiveAsync(player, token).ConfigureAwait(false);
        return round is null ? null : View(round);
    }

    public async ValueTask<DurableCasinoRoundReceipt> ApplyAsync(DurableCasinoRoundOperation operation, CancellationToken token = default)
    {
        var maximumStake = operation.Game == CasinoRoundGame.BiggestBoy ? BiggestBoyStakeLimits.Maximum : CasinoStakeLimits.TechnicalMaximum;
        var stakeValid = operation.Game == CasinoRoundGame.BiggestBoy
            ? BiggestBoyStakeLimits.IsValid(operation.Stake) && operation.Stake <= maximumStake
            : operation.Stake >= 50 && operation.Stake <= maximumStake && operation.Stake % 50 == 0;
        if (operation.Id == Guid.Empty || operation.SessionId == Guid.Empty || operation.Player is null
            || !Enum.IsDefined(operation.Game) || !Enum.IsDefined(operation.Action)
            || !stakeValid || operation.ExpectedRevision < 0
            || (operation.Action == CasinoRoundAction.Start) != (operation.ExpectedRevision == 0))
            throw new CasinoRejectedException("Ungültige Spielaktion.");
        var kind = operation.Game switch
        {
            CasinoRoundGame.Blackjack => CasinoRoundKind.Blackjack,
            CasinoRoundGame.Risk => CasinoRoundKind.Risk,
            CasinoRoundGame.Spitze => CasinoRoundKind.Spitze,
            CasinoRoundGame.BiggestBoy => CasinoRoundKind.BiggestBoy,
            _ => CasinoRoundKind.BonusSlot,
        };
        var request = new CasinoRoundOperation(operation.Id, operation.SessionId, operation.Player,
            kind, operation.ExpectedRevision, JsonSerializer.Serialize(new { operation.Action, operation.Stake, operation.Choice }));
        var receipt = await store.ApplyAsync(request, current => Plan(operation, current),
            current => Transition(operation, current), token).ConfigureAwait(false);
        return new(receipt.OperationId, receipt.Balance, View(receipt.Round, operation.Stake));
    }

    private CasinoRoundPlan Plan(DurableCasinoRoundOperation op, CasinoRoundSnapshot? current)
    {
        if (current is null)
        {
            if (op.Game == CasinoRoundGame.Spitze && op.Action != CasinoRoundAction.Start)
                throw new CasinoRejectedException("Alles Spitze muss mit START beginnen.");
            var maximum = op.Game == CasinoRoundGame.BiggestBoy ? Math.Clamp(biggestBoyMaximumStake, 50, _biggestBoyOutcomes is null ? BiggestBoyRules.MaximumStake : BiggestBoyStakeLimits.Maximum) : _stakeLimits.For(op.Game);
            if (op.Game == CasinoRoundGame.BiggestBoy && _biggestBoyOutcomes is null && !BiggestBoyRules.IsValidStake(op.Stake))
                throw new CasinoRejectedException("Ungültiger Einsatz für historische Spielregeln.");
            if (op.Stake > maximum) throw new CasinoRejectedException("Konfiguriertes Einsatzlimit überschritten.");
            var newVersion = op.Game == CasinoRoundGame.BiggestBoy && _biggestBoyOutcomes is not null
                ? ControlledSlotRules.BiggestVersion : op.Game == CasinoRoundGame.Book && _bookOutcomes is not null
                    ? ControlledSlotRules.BookVersion : null;
            var newSettings = op.Game == CasinoRoundGame.BiggestBoy ? _biggestBoyOutcomes
                : op.Game == CasinoRoundGame.Book ? _bookOutcomes : null;
            return new(op.Stake, Exposure(op.Game, op.Stake, newVersion, newSettings));
        }
        var view = View(current);
        if (view.Game != op.Game || view.Stake != op.Stake || (current.RulesVersion != Version(op.Game)
                && !ControlledSlotRules.IsControlledBiggest(current.RulesVersion) && !ControlledSlotRules.IsControlledBook(current.RulesVersion)
                && !(op.Game == CasinoRoundGame.Blackjack && BlackjackRules.Supports(current.RulesVersion))
                && !(op.Game == CasinoRoundGame.BiggestBoy && current.RulesVersion is BiggestBoyRules.Version or BiggestBoyCollectorRules.Version)))
            throw new CasinoRejectedException("Einsatz oder gespeicherte Spielregeln stimmen nicht überein.");
        long debit = 0;
        switch (op.Game)
        {
            case CasinoRoundGame.Blackjack:
                var blackjack = Read<BlackjackState>(current);
                if (blackjack.RulesVersion != current.RulesVersion)
                    throw new CasinoRejectedException("Gespeicherte Blackjack-Regeln sind widersprüchlich.");
                var action = BlackjackActionFor(op.Action);
                if (!BlackjackRules.LegalActions(blackjack).Contains(action))
                    throw new CasinoRejectedException("Diese Blackjack-Aktion ist nicht möglich.");
                if (action is BlackjackAction.Double or BlackjackAction.Split)
                    debit = blackjack.PlayerHands[blackjack.ActiveHandIndex].Stake;
                if (action == BlackjackAction.Insurance) debit = blackjack.InitialStake / 2;
                break;
            case CasinoRoundGame.Risk:
                var risk = Read<RiskRoundState>(current);
                if (op.Action is not (CasinoRoundAction.Bank or CasinoRoundAction.Risk)
                    || risk.Status != RiskRoundStatus.Active
                    || op.Action == CasinoRoundAction.Risk && (risk.Gambles >= RiskRules.MaximumGambles
                        || risk.HeldPayout > RiskRules.MaximumHeldExposure / 2))
                    throw new CasinoRejectedException("Diese Risk-Aktion ist nicht möglich.");
                break;
            case CasinoRoundGame.Spitze:
                var spitze = Read<SpitzeRoundState>(current);
                if (spitze.RulesVersion != current.RulesVersion || spitze.Stake != op.Stake
                    || spitze.Status != SpitzeRoundStatus.Active)
                    throw new CasinoRejectedException("Alles-Spitze-Spielstand ist ungültig.");
                if (op.Action == CasinoRoundAction.Spin
                    && spitze.ActionGamesRemaining == 0 && spitze.PaidSpins < SpitzeRules.MaximumPaidSpins)
                    debit = op.Stake;
                else if (op.Action == CasinoRoundAction.ActionGame && spitze.ActionGamesRemaining > 0
                    || op.Action == CasinoRoundAction.Collect)
                    debit = 0;
                else
                    throw new CasinoRejectedException("Diese Alles-Spitze-Aktion ist nicht möglich.");
                break;
            case CasinoRoundGame.BiggestBoy:
                var biggest = Read<BiggestBoySpinResult>(current);
                if (biggest.State.RulesVersion != current.RulesVersion)
                    throw new CasinoRejectedException("Gespeicherte Biggest-Boy-Regeln sind widersprüchlich.");
                if (biggest.State.RulesVersion is BiggestBoyCollectorRules.Version or BiggestBoyExpandingRules.Version or ControlledSlotRules.BiggestVersion or ControlledSlotRules.LegacyBiggestVersion)
                {
                    if (op.Action != CasinoRoundAction.FreeSpin || biggest.State.FreeSpinsRemaining <= 0)
                        throw new CasinoRejectedException("Keine Freispiele verfügbar.");
                }
                else if (op.Action == CasinoRoundAction.SelectSymbol)
                {
                    if (biggest.State.FreeSpinsRemaining <= 0 || biggest.State.ExpandingSymbol is not null
                        || !Enum.IsDefined((BiggestBoySymbol)op.Choice)
                        || !BiggestBoyRules.IsSelectableExpandingSymbol((BiggestBoySymbol)op.Choice))
                        throw new CasinoRejectedException("Dieses Bonussymbol kann nicht gewählt werden.");
                }
                else if (op.Action != CasinoRoundAction.FreeSpin || biggest.State.FreeSpinsRemaining <= 0
                    || biggest.State.ExpandingSymbol is null)
                    throw new CasinoRejectedException(biggest.State.ExpandingSymbol is null
                        ? "Vor dem Freispiel muss ein Bonussymbol gewählt werden."
                        : "Keine Freispiele verfügbar.");
                break;
            case CasinoRoundGame.Book:
                if (op.Action != CasinoRoundAction.FreeSpin || Read<BonusSlotSpinResult>(current).State.FreeSpinsRemaining <= 0)
                    throw new CasinoRejectedException("Keine Freispiele verfügbar.");
                break;
        }
        // Reserve against the snapshot's frozen paytable, not current server settings.
        var frozenSettings = op.Game switch
        {
            CasinoRoundGame.BiggestBoy when ControlledSlotRules.IsControlledBiggest(current.RulesVersion)
                => Read<BiggestBoySpinResult>(current).State.OutcomeSettings,
            CasinoRoundGame.Book when ControlledSlotRules.IsControlledBook(current.RulesVersion)
                => Read<BonusSlotSpinResult>(current).State.OutcomeSettings,
            _ => null,
        };
        return new(debit, Exposure(op.Game, op.Stake, current.RulesVersion, frozenSettings));
    }

    private CasinoRoundMutation Transition(DurableCasinoRoundOperation op, CasinoRoundSnapshot? current)
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
                debit = BlackjackRules.TotalStake(blackjack) - (before is null ? 0 : BlackjackRules.TotalStake(before));
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
            case CasinoRoundGame.Spitze:
                var previousSpitze = current is null ? null : Read<SpitzeRoundState>(current);
                var spitze = previousSpitze is null
                    ? SpitzeRules.Start(op.Stake, SpitzeRules.FromRoll(RandomNumberGenerator.GetInt32(100)))
                    : op.Action switch
                    {
                        CasinoRoundAction.Spin => SpitzeRules.Spin(previousSpitze, previousSpitze.Revision,
                            SpitzeRules.FromRoll(RandomNumberGenerator.GetInt32(100))),
                        CasinoRoundAction.ActionGame => SpitzeRules.FreeSpin(previousSpitze, previousSpitze.Revision,
                            SpitzeRules.FromRoll(RandomNumberGenerator.GetInt32(100))),
                        CasinoRoundAction.Collect => SpitzeRules.Collect(previousSpitze, previousSpitze.Revision),
                        _ => throw new CasinoRejectedException("Diese Alles-Spitze-Aktion ist nicht möglich."),
                    };
                state = spitze;
                complete = spitze.Status != SpitzeRoundStatus.Active;
                payout = spitze.FinalPayout ?? 0;
                debit = previousSpitze is null || op.Action == CasinoRoundAction.Spin ? op.Stake : 0;
                break;
            case CasinoRoundGame.BiggestBoy:
                var previousBiggest = current is null ? null : Read<BiggestBoySpinResult>(current);
                var biggest = previousBiggest is null ? _biggestBoyOutcomes is { } controls
                    ? ControlledSlotRules.Biggest(op.Stake, RandomNumberGenerator.GetInt32, controls)
                    : BiggestBoyExpandingRules.StartRound(op.Stake, RandomNumberGenerator.GetInt32, _biggestBoyProfile)
                    : ControlledSlotRules.IsControlledBiggest(previousBiggest.State.RulesVersion)
                        ? ControlledSlotRules.Biggest(op.Stake, RandomNumberGenerator.GetInt32, previousBiggest.State.OutcomeSettings!, previousBiggest.State)
                    : previousBiggest.State.RulesVersion == BiggestBoyExpandingRules.Version
                        ? BiggestBoyExpandingRules.PlayFreeSpin(previousBiggest.State, RandomNumberGenerator.GetInt32)
                    : previousBiggest.State.RulesVersion == BiggestBoyCollectorRules.Version
                        ? BiggestBoyCollectorRules.PlayFreeSpin(previousBiggest.State, RandomNumberGenerator.GetInt32)
                    : op.Action == CasinoRoundAction.SelectSymbol
                        ? BiggestBoyRules.SelectExpandingSymbol(previousBiggest, (BiggestBoySymbol)op.Choice)
                        : BiggestBoyRules.PlayFreeSpin(previousBiggest.State, RandomNumberGenerator.GetInt32);
                state = biggest;
                complete = biggest.State.FreeSpinsRemaining == 0;
                payout = biggest.State.TotalPayout;
                debit = biggest.ChargedStake;
                break;
            default:
                var previousBook = current is null ? null : Read<BonusSlotSpinResult>(current);
                var bonus = previousBook is null ? _bookOutcomes is { } bookControls
                    ? ControlledSlotRules.Book(op.Stake, RandomNumberGenerator.GetInt32, bookControls)
                    : BonusSlotRules.StartRound(op.Stake, RandomNumberGenerator.GetInt32)
                    : ControlledSlotRules.IsControlledBook(previousBook.State.RulesVersion)
                        ? ControlledSlotRules.Book(op.Stake, RandomNumberGenerator.GetInt32, previousBook.State.OutcomeSettings!, previousBook.State)
                        : BonusSlotRules.PlayFreeSpin(previousBook.State, RandomNumberGenerator.GetInt32);
                state = bonus;
                complete = bonus.State.FreeSpinsRemaining == 0;
                payout = bonus.State.TotalPayout;
                debit = bonus.ChargedStake;
                break;
        }
        var json = JsonSerializer.Serialize(state, state.GetType());
        var rulesVersion = state is BiggestBoySpinResult biggestState ? biggestState.State.RulesVersion : state is BonusSlotSpinResult bookState ? bookState.State.RulesVersion : state is BlackjackState blackjackState ? blackjackState.RulesVersion : Version(op.Game);
        var frozenSettings = state is BiggestBoySpinResult biggestSnapshot ? biggestSnapshot.State.OutcomeSettings
            : state is BonusSlotSpinResult bookSnapshot ? bookSnapshot.State.OutcomeSettings : null;
        return new(rulesVersion, json, debit, complete ? payout : 0,
            complete ? 0 : Exposure(op.Game, op.Stake, rulesVersion, frozenSettings), complete, "{}");
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
                totalStake = BlackjackRules.TotalStake(blackjack);
                payout = blackjack.TotalPayout;
                break;
            case CasinoRoundKind.Risk:
                var risk = Read<RiskRoundState>(round);
                game = CasinoRoundGame.Risk;
                stake = risk.HeldPayout == 0 ? fallbackStake : checked((int)(risk.HeldPayout / (1L << risk.Gambles)));
                totalStake = stake;
                payout = risk.FinalPayout ?? 0;
                break;
            case CasinoRoundKind.Spitze:
                var spitze = Read<SpitzeRoundState>(round);
                game = CasinoRoundGame.Spitze;
                stake = spitze.Stake;
                totalStake = checked((long)spitze.Stake * spitze.PaidSpins);
                payout = spitze.FinalPayout ?? 0;
                break;
            case CasinoRoundKind.BiggestBoy:
                var biggest = Read<BiggestBoySpinResult>(round);
                game = CasinoRoundGame.BiggestBoy;
                stake = biggest.State.LockedStake;
                totalStake = stake;
                payout = biggest.State.TotalPayout;
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
        CasinoRoundGame.Spitze => SpitzeRules.Version,
        CasinoRoundGame.BiggestBoy => BiggestBoyExpandingRules.Version,
        _ => BonusSlotRules.Version,
    };
    private static long Exposure(CasinoRoundGame game, int stake, string? version = null,
        SlotOutcomeSettings? settings = null) => game switch
    {
        CasinoRoundGame.Blackjack => stake * (version == BlackjackRules.LegacyVersion ? 8L : 17L),
        CasinoRoundGame.Risk => Math.Min(RiskRules.MaximumHeldExposure, stake * (1L << RiskRules.MaximumGambles)),
        CasinoRoundGame.Spitze => SpitzeRules.MaximumExposure(stake),
        CasinoRoundGame.BiggestBoy => version switch
        {
            ControlledSlotRules.BiggestVersion => BiggestBoyBalancedRules.MaximumRoundExposure(stake, settings?.Paytable),
            BiggestBoyRules.Version => BiggestBoyRules.MaximumRoundExposure(stake),
            BiggestBoyCollectorRules.Version => BiggestBoyCollectorRules.MaximumRoundExposure(stake),
            _ => BiggestBoyExpandingRules.MaximumRoundExposure(stake),
        },
        _ => version == ControlledSlotRules.BookVersion
            ? BookBalancedRules.MaximumRoundExposure(stake, settings?.Paytable)
            : BonusSlotRules.MaximumRoundExposure(stake),
    };
    private static BlackjackAction BlackjackActionFor(CasinoRoundAction action) => action switch
    {
        CasinoRoundAction.Hit => BlackjackAction.Hit,
        CasinoRoundAction.Stand => BlackjackAction.Stand,
        CasinoRoundAction.Double => BlackjackAction.Double,
        CasinoRoundAction.Split => BlackjackAction.Split,
        CasinoRoundAction.Insurance => BlackjackAction.Insurance,
        CasinoRoundAction.DeclineInsurance => BlackjackAction.DeclineInsurance,
        CasinoRoundAction.Surrender => BlackjackAction.Surrender,
        CasinoRoundAction.EvenMoney => BlackjackAction.EvenMoney,
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
