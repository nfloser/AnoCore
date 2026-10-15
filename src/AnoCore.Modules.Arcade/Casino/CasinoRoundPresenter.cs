using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Modules.Arcade.Casino;

/// <summary>Native view of durable rounds. Local state owns presentation and retries, never money.</summary>
public sealed class CasinoRoundPresenter : IDisposable
{
    public static readonly CustomHudId HudId = new("ano.casino.rounds");
    public static readonly CustomHudId ExpandingHudId = new("ano.casino.biggestboy");
    private static readonly PermissionId Permission = new("ano.casino.play");
    private static readonly string[] Buttons = ["play", "minus", "plus", "hit", "stand", "double", "split", "risk", "bank", "free", "bonus1", "bonus2", "bonus3", "bonus4", "bonus5", "bonus6", "bonus7", "bonus8", "bonus9", "retry", "close", "back", "refresh", "rules", "sound"];
    private readonly ICustomHudService _hud;
    private readonly IPlayerRegistry _players;
    private readonly IPermissionEvaluator _permissions;
    private readonly ICasinoWallet _wallet;
    private readonly ICasinoRounds _rounds;
    private readonly Action<PlayerId> _beforeOpen;
    private readonly Func<PlayerId, CancellationToken, ValueTask> _back;
    private readonly Action<PlayerId>? _reelStopSound;
    private readonly TimeProvider _clock;
    private readonly int _biggestBoyMaximumStake;
    private readonly CasinoStakeLimits _stakeLimits;
    private readonly string _biggestBoyProfile;
    private readonly object _gate = new();
    private readonly Dictionary<PlayerId, State> _states = [];
    private readonly List<IDisposable> _owned = [];
    private bool _disposed;

    public CasinoRoundPresenter(ICustomHudService hud, IPlayerRegistry players, IPermissionEvaluator permissions,
        ICasinoWallet wallet, ICasinoRounds rounds, IAnoEventBus events, Action<PlayerId> beforeOpen,
        Func<PlayerId, CancellationToken, ValueTask> back, TimeProvider? clock = null, int biggestBoyMaximumStake = BiggestBoyRules.MaximumStake,
        string biggestBoyProfile = BiggestBoyProfiles.Standard, CasinoStakeLimits? stakeLimits = null,
        Action<PlayerId>? reelStopSound = null)
    {
        _stakeLimits = (stakeLimits ?? new()).Validate();
        _hud = hud; _players = players; _permissions = permissions; _wallet = wallet; _rounds = rounds;
        _beforeOpen = beforeOpen; _back = back; _reelStopSound = reelStopSound; _clock = clock ?? TimeProvider.System;
        _biggestBoyProfile = BiggestBoyProfiles.Validate(biggestBoyProfile);
        _biggestBoyMaximumStake = Math.Clamp(biggestBoyMaximumStake, BiggestBoyRules.StakeSteps[0], BiggestBoyRules.MaximumStake);
        try
        {
            _owned.Add(hud.Register(new("ano.casino"), new(HudId,
                "panorama/layout/custom_game/anocore/ano_casino_rounds.xml", "casino_round_root",
                Buttons.Select(x => "casino_round_" + x).ToArray(), captureInput: true), ClickAsync));
            _owned.Add(hud.Register(new("ano.casino"), new(ExpandingHudId,
                "panorama/layout/custom_game/anocore/ano_casino_biggest.xml", "casino_round_root",
                new[] { "play", "minus", "plus", "free", "retry", "close", "back", "refresh", "rules", "sound" }
                    .Select(x => "casino_round_" + x).ToArray(), captureInput: true), ClickAsync));
            _owned.Add(events.Subscribe<PlayerDisconnectedEvent>((e, _) => { Forget(e.Player); return ValueTask.CompletedTask; }));
            _owned.Add(events.Subscribe<PlayerReconnectedEvent>((e, _) => { Forget(e.Previous); return ValueTask.CompletedTask; }));
        }
        catch { foreach (var owned in _owned) owned.Dispose(); throw; }
    }

    public async ValueTask<bool> OpenAsync(PlayerId player, CasinoRoundGame game, CancellationToken token = default)
    {
        if (!Enum.IsDefined(game) || !await _permissions.HasPermissionAsync(player, Permission, token).ConfigureAwait(false)) return false;
        State state;
        lock (_gate)
        {
            if (_disposed || !_players.TryGet(player, out var p) || p is not { IsConnected: true }) return false;
            if (!_states.TryGetValue(player, out state!) || state.Player.SessionId != p.SessionId) _states[player] = state = new(p, game);
            if (state.Pending) return false;
            state.Pending = true;
        }
        try
        {
            var round = await _rounds.ReadAsync(player, token).ConfigureAwait(false);
            var balance = await _wallet.ReadAsync(player, token).ConfigureAwait(false);
            if (!await _permissions.HasPermissionAsync(player, Permission, token).ConfigureAwait(false)) return false;
            lock (_gate)
            {
                if (!Current(state)) return false;
                state.Game = round is { IsComplete: false } ? round.Game : game;
                state.Round = round?.Game == state.Game ? round : null;
                state.Stake = state.Round?.Stake ?? (state.Game == CasinoRoundGame.BiggestBoy ? BiggestBoyRules.ClampStakeToConfiguredMaximum(state.Stake, _biggestBoyMaximumStake) : Math.Min(state.Stake, _stakeLimits.For(state.Game)));
                state.Balance = balance; state.Visible = true; state.Pending = false;
                state.Status = round is { IsComplete: false } ? "Gespeicherte Runde fortsetzen" : "Einsatz wählen und Runde starten";
                state.StatusError = false;
                _beforeOpen(player); Render(state);
                return _hud.Show(player, state.Hud);
            }
        }
        finally { lock (_gate) state.Pending = false; }
    }

    private async ValueTask ClickAsync(CustomHudClickContext context)
    {
        State state;
        lock (_gate) if (_disposed || !_states.TryGetValue(context.PlayerId, out state!) || !state.Visible) return;
        var allowed = await _permissions.HasPermissionAsync(context.PlayerId, Permission, context.CancellationToken).ConfigureAwait(false);
        DurableCasinoRoundOperation? operation = null; var back = false; var closed = false;
        lock (_gate)
        {
            if (!Current(state) || !state.Visible || context.HudId != state.Hud) return;
            if (!allowed) { Forget(state.Player); return; }
            var action = context.ButtonId.StartsWith("casino_round_", StringComparison.Ordinal) ? context.ButtonId[13..] : "";
            if (!Buttons.Contains(action, StringComparer.Ordinal)) return;
            if (action == "sound") { state.ReelSoundEnabled = !state.ReelSoundEnabled; Render(state); return; }
            if (action is "close" or "back") { Close(context.PlayerId); back = action == "back"; closed = true; }
            else
            {
                if (state.Pending || state.AnimatingUntil is not null) return;
                if (state.Unconfirmed is not null && action is not ("retry" or "rules"))
                { state.Status = "Unbestätigte Aktion: BUCHUNG PRÜFEN verwenden."; Render(state); return; }
                if (action == "rules") { state.BiggestRules = !state.BiggestRules; state.Status = state.Game is CasinoRoundGame.BiggestBoy or CasinoRoundGame.Book ? state.BiggestRules ? "Gewinnplan geöffnet · REGELN schließt die Ansicht" : "Gewinnplan geschlossen" : Rules(state.Game); Render(state); return; }
                if (action is "minus" or "plus")
                {
                    if (state.Round is { IsComplete: false }) return;
                    state.Stake = state.Game == CasinoRoundGame.BiggestBoy
                        ? BiggestBoyRules.StepStake(state.Stake, action == "plus" ? 1 : -1, _biggestBoyMaximumStake)
                        : Math.Clamp(state.Stake + (action == "plus" ? 50 : -50), 50, _stakeLimits.For(state.Game));
                    Render(state); return;
                }
                if (action == "retry") operation = state.Unconfirmed;
                else if (action == "play" && state.Round is not { IsComplete: false })
                    operation = new(Guid.NewGuid(), context.PlayerId, Guid.NewGuid(), state.Game, CasinoRoundAction.Start, state.Stake, 0);
                else if (state.Round is { IsComplete: false } round && action.StartsWith("bonus", StringComparison.Ordinal)
                    && int.TryParse(action.AsSpan(5), out var bonusChoice))
                    operation = new(Guid.NewGuid(), context.PlayerId, round.Id, round.Game, CasinoRoundAction.SelectSymbol,
                        round.Stake, round.Revision, bonusChoice);
                else if (state.Round is { IsComplete: false } activeRound && ParseAction(action) is { } roundAction)
                    operation = new(Guid.NewGuid(), context.PlayerId, activeRound.Id, activeRound.Game, roundAction, activeRound.Stake, activeRound.Revision);
                if (operation is null && action != "refresh") return;
                state.StatusError = false;
                state.Pending = true;
                if (operation is not null) state.Unconfirmed = operation;
                Render(state);
            }
        }
        if (back) { await _back(context.PlayerId, context.CancellationToken).ConfigureAwait(false); return; }
        if (closed || !state.Pending) return;
        try
        {
            var receipt = operation is null ? null : await _rounds.ApplyAsync(operation, context.CancellationToken).ConfigureAwait(false);
            var round = receipt?.Round ?? await _rounds.ReadAsync(context.PlayerId, context.CancellationToken).ConfigureAwait(false);
            var balance = await _wallet.ReadAsync(context.PlayerId, context.CancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (!Current(state)) return;
                var previousSpin = state.Round is { Game: CasinoRoundGame.BiggestBoy } previousRound
                    ? Read<BiggestBoySpinResult>(previousRound) : null;
                var previousStops = previousSpin?.State.RulesVersion is BiggestBoyExpandingRules.Version or ControlledSlotRules.BiggestVersion
                    ? previousSpin.Stops : ImmutableArray<int>.Empty;
                state.Round = round; state.Balance = balance;
                state.StatusError = false;
                if (round is not null)
                {
                    state.Game = round.Game;
                    if (round.IsComplete) state.Stake = state.Game == CasinoRoundGame.BiggestBoy ? BiggestBoyRules.ClampStakeToConfiguredMaximum(state.Stake, _biggestBoyMaximumStake) : Math.Min(state.Stake, _stakeLimits.For(state.Game));
                }
                if (receipt is not null)
                {
                    state.Unconfirmed = null;
                    if (operation?.Action == CasinoRoundAction.SelectSymbol)
                    {
                        state.AnimatingUntil = null;
                        state.AnimationStartsAt = null;
                        state.Status = "Bonussymbol gewählt · Freispiele bereit";
                    }
                    else
                    {
                        state.Animation++;
                        if (operation?.Action == CasinoRoundAction.Start) state.Cards.Clear();
                        if (round is not { Game: CasinoRoundGame.BiggestBoy } || !ControlledSlotRules.IsBiggest(Read<BiggestBoySpinResult>(round).State.RulesVersion))
                            for (var reel = 0; reel < 5; reel++)
                            {
                                Class(state, "reel" + reel, "stopped", false);
                                Class(state, "reel" + reel, "settling", false);
                                Class(state, "biggest_reel" + reel, "stopped", false);
                                Class(state, "biggest_reel" + reel, "settling", false);
                            }
                        state.BookSettles = 0; state.BookStops = 0; state.SoundStops = 0;
                        state.WinCycle = 0; state.WinCycleAt = null;
                        if (round is { Game: CasinoRoundGame.Book } ||
                            round is { Game: CasinoRoundGame.BiggestBoy } && ControlledSlotRules.IsBiggest(Read<BiggestBoySpinResult>(round).State.RulesVersion))
                        {
                            state.PreviousStops = previousStops.Length == 5 ? previousStops.ToArray() : new int[5];
                            Array.Fill(state.MotionProgress, -1);
                            Array.Fill(state.MotionFrames, 0);
                            state.CosmeticMotion = round.Game == CasinoRoundGame.BiggestBoy
                                && Read<BiggestBoySpinResult>(round).State.RulesVersion == ControlledSlotRules.BiggestVersion
                                ? CosmeticReelMotion.Create(12, Read<BiggestBoySpinResult>(round).RawGrid.Select(x => (int)x).ToArray(), Random.Shared.Next)
                                : null;
                        }
                        var now = _clock.GetUtcNow();
                        state.AnimationStartsAt = state.Game is CasinoRoundGame.Book or CasinoRoundGame.BiggestBoy
                            ? now.AddMilliseconds(200) : null;
                        var v4 = round is { Game: CasinoRoundGame.BiggestBoy } && ControlledSlotRules.IsBiggest(Read<BiggestBoySpinResult>(round).State.RulesVersion);
                        state.AnimatingUntil = (state.AnimationStartsAt ?? now).AddSeconds(v4 || state.Game == CasinoRoundGame.Book
                            ? BiggestBoyReelMotion.TotalDurationSeconds : state.Game == CasinoRoundGame.BiggestBoy ? 3.2 : state.Game == CasinoRoundGame.Blackjack ? .95 : .65);
                        state.Status = "Aktion gebucht · Animation läuft";
                    }
                }
                else state.Status = "Spielstand aktualisiert";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate) if (Current(state))
            {
                if (ex is ArgumentException or CasinoRejectedException) state.Unconfirmed = null;
                state.Status = ex is ArgumentException or CasinoRejectedException ? ex.Message
                    : "Buchung unbestätigt. BUCHUNG PRÜFEN verwendet dieselbe Aktions-ID.";
                state.StatusError = true;
            }
        }
        finally { lock (_gate) { state.Pending = false; if (Current(state) && state.Visible) Render(state); } }
    }

    public void Tick()
    {
        lock (_gate) foreach (var state in _states.Values.ToArray())
        {
            if (!Current(state)) { Forget(state.Player); continue; }
            if (state.AnimationStartsAt is { } start && _clock.GetUtcNow() >= start)
            {
                state.AnimationStartsAt = null;
                if (state.AnimatingUntil is { } animationEnd && _clock.GetUtcNow() < animationEnd && state.Visible) Render(state);
            }
            if (state.Hud != ExpandingHudId && (state.Game is CasinoRoundGame.Book or CasinoRoundGame.BiggestBoy) && state.AnimatingUntil is { } bookEnd)
                for (var reel = 0; reel < 5; reel++)
                {
                    var reelId = (state.Game == CasinoRoundGame.BiggestBoy ? "biggest_reel" : "reel") + reel;
                    var stop = bookEnd.AddSeconds(-1 + reel * .2);
                    var now = _clock.GetUtcNow();
                    if ((state.BookSettles & (1 << reel)) == 0 && now >= stop.AddSeconds(-.45) && now < stop)
                    {
                        state.BookSettles |= 1 << reel;
                        Class(state, reelId, "settling", true);
                    }
                    if ((state.BookStops & (1 << reel)) == 0 && now >= stop)
                    {
                        state.BookStops |= 1 << reel;
                        Class(state, reelId, "settling", false);
                        Class(state, reelId, "stopped", true);
                    }
                }
            // One click per reel when its visual reel actually stops, only to this
            // player. A late Tick catches up missing stops once without replaying.
            if (state.Hud == ExpandingHudId && state.AnimationStartsAt is null
                && state.AnimatingUntil is { } soundEnd && state.Visible)
            {
                var motionStarted = soundEnd.AddSeconds(-BiggestBoyReelMotion.TotalDurationSeconds);
                var elapsed = (_clock.GetUtcNow() - motionStarted).TotalSeconds;
                for (var reel = 0; reel < 5; reel++)
                {
                    var bit = 1 << reel;
                    if ((state.SoundStops & bit) != 0 || elapsed < BiggestBoyReelMotion.Duration(reel)) continue;
                    state.SoundStops |= bit;
                    if (state.ReelSoundEnabled) _reelStopSound?.Invoke(state.Player.Id);
                }
            }
            // Four visible symbols per reel advance on a cyclic strip; no huge off-screen panels.
            if (state.Hud == ExpandingHudId && state.AnimationStartsAt is null
                && state.AnimatingUntil is { } movingEnd && _clock.GetUtcNow() < movingEnd && state.Visible)
                Render(state);
            if (state.Hud == ExpandingHudId && state.AnimatingUntil is null && state.Round is { } won
                && state.Visible && state.WinCycleAt is { } next && _clock.GetUtcNow() >= next)
            {
                var winCount = state.Game == CasinoRoundGame.Book
                    ? Read<BonusSlotSpinResult>(won).LineWins.Length : Read<BiggestBoySpinResult>(won).LineWins.Length;
                if (winCount > 1) { state.WinCycle = (state.WinCycle + 1) % winCount; state.WinCycleAt = _clock.GetUtcNow().AddSeconds(1.2); Render(state); }
                else state.WinCycleAt = null;
            }
            if (state.AnimatingUntil is { } end && _clock.GetUtcNow() >= end)
            { state.AnimatingUntil = null; state.AnimationStartsAt = null; state.Status = "Gespeichertes Ergebnis"; state.WinCycleAt = _clock.GetUtcNow().AddSeconds(1.2); if (state.Visible) Render(state); }
        }
    }

    private void Render(State state)
    {
        var expanding = state.Game == CasinoRoundGame.Book || state.Game == CasinoRoundGame.BiggestBoy && (state.Round is null
            || ControlledSlotRules.IsBiggest(Read<BiggestBoySpinResult>(state.Round).State.RulesVersion));
        var desired = expanding ? ExpandingHudId : HudId;
        var changed = state.Hud != desired;
        if (changed) { _hud.Hide(state.Player.Id, state.Hud); state.Classes.Clear(); state.Hud = desired; }
        if (expanding) RenderExpanding(state); else RenderLegacy(state);
        if (expanding) Text(state, "sound_text", state.ReelSoundEnabled ? "WALZENTON: AN" : "WALZENTON: AUS");
        if (changed && state.Visible) _hud.Show(state.Player.Id, state.Hud);
    }

    private void RenderLegacy(State state)
    {
        foreach (var game in Enum.GetValues<CasinoRoundGame>()) Class(state, "root", "view-" + game.ToString().ToLowerInvariant(), state.Game == game);
        Class(state, "root", "biggest-rules", state.Game == CasinoRoundGame.BiggestBoy && state.BiggestRules
            && (state.Round is null || Read<BiggestBoySpinResult>(state.Round).State.RulesVersion == BiggestBoyCollectorRules.Version));
        var active = state.Round is { IsComplete: false };
        var moving = state.AnimatingUntil is not null;
        Class(state, "root", "complete", state.Round is { IsComplete: true });
        Class(state, "root", "active", active); Class(state, "root", "busy", state.Pending || moving);
        Class(state, "root", "uncertain", state.Unconfirmed is not null);
        var rolling = moving && state.AnimationStartsAt is null;
        Class(state, "root", "reels-armed", moving && !rolling);
        Class(state, "root", "motion-a", rolling && state.Animation % 2 == 1);
        Class(state, "root", "motion-b", rolling && state.Animation % 2 == 0);
        Text(state, "title", state.Game switch { CasinoRoundGame.Blackjack => "BLACKJACK", CasinoRoundGame.Book => "BOOK · FREISPIELE", CasinoRoundGame.BiggestBoy => "BIGGEST BOY · 5×3", _ => "RISK · DOUBLE OR BANK" });
        Text(state, "account", Points(state.Balance.Available)); Text(state, "bankroll", Points(state.Balance.Allocated));
        Text(state, "stake", Points(active ? state.Round!.Stake : state.Stake)); Text(state, "status", state.Status);
        Text(state, "payout", state.Round is { IsComplete: true } round && !moving ? Points(round.Payout) : "0 €");
        Class(state, "root", "can-risk", false);
        Class(state, "root", "can-double", false); Class(state, "root", "can-split", false);
        if (state.Round is not { } snapshot)
        {
            ClearCards(state);
            Text(state, "held", "0 €"); Text(state, "next", "Einsatz wählen und Runde starten");
            Text(state, "bonus", "Keine Freispiele aktiv");
            for (var step = 0; step < 5; step++) Class(state, "step" + step, "reached", false);
            Text(state, "detail", Rules(state.Game));
            Text(state, "dealer", "Dealer wartet auf den Rundenstart");
            if (state.Game == CasinoRoundGame.BiggestBoy) RenderBiggestBoy(state, null, false);
            if (state.Game == CasinoRoundGame.Book)
                for (var index = 0; index < 15; index++)
                {
                    var previewSymbol = (BonusSlotSymbol)(index % 8);
                    Text(state, "symbol" + index, SymbolName(previewSymbol));
                    for (var symbol = 0; symbol < 8; symbol++)
                        Class(state, "symbol" + index, "symbol-" + symbol, (int)previewSymbol == symbol);
                }
            return;
        }
        switch (snapshot.Game)
        {
            case CasinoRoundGame.Risk:
                var risk = Read<RiskRoundState>(snapshot);
                Class(state, "root", "can-risk", !snapshot.IsComplete && risk.Gambles < RiskRules.MaximumGambles && risk.HeldPayout <= RiskRules.MaximumHeldExposure / 2);
                Text(state, "held", Points(snapshot.IsComplete ? snapshot.Payout : risk.HeldPayout));
                Text(state, "next", snapshot.IsComplete ? "Runde beendet" : risk.Gambles >= RiskRules.MaximumGambles || risk.HeldPayout > RiskRules.MaximumHeldExposure / 2 ? "Maximum erreicht · BANK wählen" : "Nächste Stufe: " + Points(risk.HeldPayout * 2L));
                for (var step = 0; step < 5; step++) Class(state, "step" + step, "reached", risk.Gambles > step);
                Text(state, "detail", $"Zwischenstand: {Points(risk.HeldPayout)} · Stufe {risk.Gambles}/5 · Chance 50 % · nächste Stufe {Points(risk.HeldPayout * 2L)}"); break;
            case CasinoRoundGame.Blackjack:
                var blackjack = Read<BlackjackState>(snapshot);
                RenderCards(state, blackjack, moving);
                Text(state, "dealer", "Dealer: " + Cards((blackjack.Status == BlackjackRoundStatus.Settled) ? blackjack.DealerCards : blackjack.DealerCards.Take(1)) + ((blackjack.Status == BlackjackRoundStatus.Settled) ? $" ({new BlackjackHand(blackjack.DealerCards, 0, true, false, false).BestTotal})" : " · verdeckt"));
                Text(state, "detail", string.Join("\n", blackjack.PlayerHands.Select((hand, index) => $"{(index == blackjack.ActiveHandIndex ? "▶ " : "")}Hand {index + 1}: {Cards(hand.Cards)} ({hand.BestTotal}) · {Points(hand.Stake)}")));
                if (!(blackjack.Status == BlackjackRoundStatus.Settled))
                {
                    Class(state, "root", "can-double", BlackjackRules.LegalActions(blackjack).Contains(BlackjackAction.Double));
                    Class(state, "root", "can-split", BlackjackRules.LegalActions(blackjack).Contains(BlackjackAction.Split));
                }
                break;
            case CasinoRoundGame.BiggestBoy:
                RenderBiggestBoy(state, Read<BiggestBoySpinResult>(snapshot), moving);
                break;
            case CasinoRoundGame.Book:
                var book = Read<BonusSlotSpinResult>(snapshot);
                Text(state, "bonus", $"{book.State.FreeSpinsRemaining} Freispiele · gesammelt {Points(book.State.TotalPayout)} · Bonussymbol: {(book.State.ExpandingSymbol is { } bonusSymbol ? SymbolName(bonusSymbol) : "–")}");
                Text(state, "detail", $"Freispiele: {book.State.FreeSpinsRemaining} · Spins: {book.State.SpinsPlayed} · gesammelte Auszahlung: {Points(book.State.TotalPayout)} · Bonussymbol: {(book.State.ExpandingSymbol?.ToString() ?? "–")}");
                var symbols = book.Grid;
                for (var index = 0; index < symbols.Length; index++)
                {
                    Text(state, "symbol" + index, SymbolName(symbols[index]));
                    for (var symbol = 0; symbol < 8; symbol++) Class(state, "symbol" + index, "symbol-" + symbol, (int)symbols[index] == symbol);
                    Class(state, "symbol" + index, "scatter", symbols[index] == BonusSlotSymbol.Book);
                    Class(state, "symbol" + index, "wild", symbols[index] == BonusSlotSymbol.Book);
                }
                break;
        }
    }

    private void RenderExpanding(State state)
    {
        if (state.Game == CasinoRoundGame.Book) { RenderBook(state); return; }
        // Both modes use the same HUD, so explicitly discard Book skin when switching games.
        Class(state, "root", "book-mode", false);
        Class(state, "root", "view-book", false);
        var spin = state.Round is null ? null : Read<BiggestBoySpinResult>(state.Round);
        Class(state, "root", "controlled-mode", spin is null || spin.State.RulesVersion == ControlledSlotRules.BiggestVersion);
        var moving = state.AnimatingUntil is not null;
        var rolling = moving && state.AnimationStartsAt is null;
        var active = state.Round is { IsComplete: false };
        Class(state, "root", "active", active); Class(state, "root", "busy", state.Pending || moving);
        Class(state, "root", "view-biggestboy", true);
        Class(state, "root", "reels-armed", moving && !rolling);
        Class(state, "root", "motion-a", rolling && state.Animation % 2 == 1);
        Class(state, "root", "motion-b", rolling && state.Animation % 2 == 0);
        Class(state, "root", "biggest-rules", state.BiggestRules);
        Class(state, "root", "uncertain", state.Unconfirmed is not null);
        Text(state, "title", "BIGGEST BOY · 5×3");
        Text(state, "account", Points(state.Balance.Available)); Text(state, "bankroll", Points(state.Balance.Allocated));
        Text(state, "stake", Points(active ? state.Round!.Stake : state.Stake));
        Text(state, "status", state.StatusError || state.Unconfirmed is not null ? state.Status : "");
        Text(state, "bankroll_footer", Points(state.Balance.Allocated));
        Text(state, "payout", moving ? "–" : Points(spin?.Payout ?? 0));
        Text(state, "biggest_win", moving ? "" : "SPINGEWINN GESAMT: " + Points(spin?.Payout ?? 0)
            + (spin is { State.SpinsPlayed: > 1 } ? " · Freispiel: " + Points(spin.Payout) : ""));
        Text(state, "biggest_spin_win", "Liniengewinn: " + (moving ? "–" : Points(spin?.LineWins.Sum(x => x.Payout) ?? 0))
            + " · Freispiel aktuell: " + (moving ? "–" : Points(spin is { State.SpinsPlayed: > 1 } ? spin.Payout : 0)));
        Text(state, "biggest_total_win", "Freispielserie: " + Points(spin?.State.BonusPayout ?? 0)
            + " · gesamte Runde: " + Points(spin?.State.TotalPayout ?? 0));
        Text(state, "biggest_bonus", $"{spin?.State.FreeSpinsRemaining ?? 0} Freispiele · Serie: {Points(spin?.State.BonusPayout ?? 0)} · Expanding Symbol: {(spin?.State.ExpandingSymbol is { } symbol ? BiggestName(symbol) : "–")}");
        Text(state, "bb_remaining", (spin?.State.FreeSpinsRemaining ?? 0).ToString(CultureInfo.InvariantCulture));
        Text(state, "bb_expanding", spin?.State.ExpandingSymbol is { } selected ? BiggestName(selected) : "–");
        Text(state, "bb_free_win", moving ? "–" : Points(spin is { State.SpinsPlayed: > 1 } ? spin.Payout : 0));
        Text(state, "bb_series", Points(spin?.State.BonusPayout ?? 0));
        var current = !moving && spin is { LineWins.Length: > 0 } ? spin.LineWins[state.WinCycle % spin.LineWins.Length] : null;
        Text(state, "detail", moving ? "" : current is not null
            ? (spin!.Grid.Distinct().Count() == 1 ? "VOLLBILD! · " : "") + $"Liniengesamt: {Points(spin.LineWins.Sum(x => x.Payout))} · Linie {current.Payline + 1}: {current.MatchCount}× {BiggestName(current.Symbol)} = {Points(current.Payout)} · {spin.LineWins.Length} Gewinnlinien"
            : spin is null ? "3/4/5 Gesichter: 10/15/20 Freispiele · Schärpe ist Wild" : "Kein Liniengewinn in diesem Spin");
        for (var reel = 0; reel < 5; reel++)
        {
            var previous = state.PreviousStops[reel];
            var stop = spin is null ? 0 : spin.Stops[reel];
            // The four-cell window follows a frozen cosmetic ribbon for v5;
            // accepted physical v4 rounds keep their original movement.
            var elapsed = moving && state.AnimatingUntil is { } finish
                ? (_clock.GetUtcNow() - finish.AddSeconds(-BiggestBoyReelMotion.TotalDurationSeconds)).TotalSeconds : 0;
            var travelled = !rolling ? 0 : state.CosmeticMotion is not null
                ? CosmeticReelMotion.Travelled(elapsed, reel)
                : BiggestBoyReelMotion.Travelled(previous, stop, elapsed, reel);
            if (state.MotionProgress[reel] != travelled)
            {
                state.MotionProgress[reel] = travelled;
                if (rolling && travelled > 0) state.MotionFrames[reel]++;
            }
            var stopped = rolling && elapsed >= BiggestBoyReelMotion.Duration(reel);
            Class(state, "bb_reel" + reel, "stopped", stopped);
            Class(state, "bb_reel" + reel, "bb-step-a", rolling && !stopped && state.MotionFrames[reel] % 2 == 1);
            Class(state, "bb_reel" + reel, "bb-step-b", rolling && !stopped && state.MotionFrames[reel] % 2 == 0 && travelled > 0);
            for (var row = 0; row < 4; row++)
            {
                var visibleSymbol = state.CosmeticMotion is { } cosmetic
                    ? (BiggestBoySymbol)cosmetic.VisibleSymbol(reel, travelled, row)
                    : BiggestBoyExpandingRules.ReelStrips[reel][BiggestBoyReelMotion.VisibleIndex(previous, travelled, row)];
                for (var id = 0; id < 12; id++)
                    Class(state, $"bb_motion{reel}_{row}", "bb-symbol-" + id, (int)visibleSymbol == id);
            }
            for (var row = 0; row < 3; row++)
            {
                var cell = reel * 3 + row;
                var actual = spin is null ? BiggestBoyExpandingRules.ReelStrips[reel][row] : spin.Grid[cell];
                for (var id = 0; id < 12; id++) Class(state, "bb_symbol" + cell, "bb-symbol-" + id, (int)actual == id);
                Class(state, "bb_symbol" + cell, "winning", !moving && spin is not null && spin.LineWins.Any(w => BiggestBoyExpandingRules.Paylines[w.Payline].Take(w.MatchCount).Contains(cell)));
                Class(state, "bb_symbol" + cell, "active-line", current is not null && BiggestBoyExpandingRules.Paylines[current.Payline].Take(current.MatchCount).Contains(cell));
                Class(state, "bb_symbol" + cell, "expanded", !moving && spin is not null && spin.ExpandedReels.Contains(reel));
            }
        }
    }


    /// <summary>Book shares the native five-reel cabinet, but never changes Book payouts or symbol assets.</summary>
    private void RenderBook(State state)
    {
        var spin = state.Round is null ? null : Read<BonusSlotSpinResult>(state.Round);
        var moving = state.AnimatingUntil is not null;
        var rolling = moving && state.AnimationStartsAt is null;
        var active = state.Round is { IsComplete: false };
        Class(state, "root", "view-biggestboy", false);
        Class(state, "root", "view-book", true);
        Class(state, "root", "book-mode", true);
        Class(state, "root", "active", active);
        Class(state, "root", "busy", state.Pending || moving);
        Class(state, "root", "reels-armed", moving && !rolling);
        Class(state, "root", "motion-a", rolling && state.Animation % 2 == 1);
        Class(state, "root", "motion-b", rolling && state.Animation % 2 == 0);
        Class(state, "root", "biggest-rules", state.BiggestRules);
        Class(state, "root", "uncertain", state.Unconfirmed is not null);
        Text(state, "title", "BOOK · FREISPIELE · 5×3");
        Text(state, "account", Points(state.Balance.Available));
        Text(state, "bankroll", Points(state.Balance.Allocated));
        Text(state, "bankroll_footer", Points(state.Balance.Allocated));
        Text(state, "stake", Points(active ? state.Round!.Stake : state.Stake));
        Text(state, "status", state.StatusError || state.Unconfirmed is not null ? state.Status : "");
        Text(state, "payout", moving ? "–" : Points(spin?.Payout ?? 0));
        Text(state, "bb_remaining", (spin?.State.FreeSpinsRemaining ?? 0).ToString(CultureInfo.InvariantCulture));
        Text(state, "bb_expanding", spin?.State.ExpandingSymbol is { } bonus ? SymbolName(bonus) : "–");
        Text(state, "bb_free_win", moving ? "–" : Points(spin is { State.SpinsPlayed: > 1 } ? spin.Payout : 0));
        Text(state, "bb_series", Points(spin is null ? 0 : spin.State.TotalPayout - spin.State.PaidSpinPayout));
        Text(state, "biggest_bonus", spin is null ? "Drei oder mehr Bücher starten Freispiele" :
            $"{spin.State.FreeSpinsRemaining} Freispiele · gesammelt {Points(spin.State.TotalPayout)}");
        Text(state, "biggest_win", moving ? "" : "SPINGEWINN: " + Points(spin?.Payout ?? 0));
        Text(state, "biggest_spin_win", moving ? "" : Points(spin?.Payout ?? 0));
        Text(state, "biggest_total_win", Points(spin?.State.TotalPayout ?? 0));
        var current = !moving && spin is { LineWins.Length: > 0 } ? spin.LineWins[state.WinCycle % spin.LineWins.Length] : null;
        Text(state, "detail", moving ? "" : current is not null
            ? (spin!.Grid.Distinct().Count() == 1 ? "VOLLBILD! · " : "") + $"Liniengesamt: {Points(spin.LineWins.Sum(x => x.Payout))} · Linie {current.Payline + 1}: {current.MatchCount}× {SymbolName(current.Symbol)} = {Points(current.Payout)}"
            : spin is null ? Rules(CasinoRoundGame.Book) : $"Spingewinn: {Points(spin.Payout)} · kumuliert: {Points(spin.State.TotalPayout)}");

        var elapsed = moving && state.AnimatingUntil is { } finish
            ? (_clock.GetUtcNow() - finish.AddSeconds(-BiggestBoyReelMotion.TotalDurationSeconds)).TotalSeconds : 0;
        for (var reel = 0; reel < 5; reel++)
        {
            var travelled = rolling ? BookReelMotion.Travelled(elapsed, reel) : 0;
            if (state.MotionProgress[reel] != travelled)
            {
                state.MotionProgress[reel] = travelled;
                if (rolling && travelled > 0) state.MotionFrames[reel]++;
            }
            var stopped = rolling && elapsed >= BiggestBoyReelMotion.Duration(reel);
            Class(state, "bb_reel" + reel, "stopped", stopped);
            Class(state, "bb_reel" + reel, "bb-step-a", rolling && !stopped && state.MotionFrames[reel] % 2 == 1);
            Class(state, "bb_reel" + reel, "bb-step-b", rolling && !stopped && state.MotionFrames[reel] % 2 == 0 && travelled > 0);
            for (var row = 0; row < 4; row++)
            {
                var visible = BookReelMotion.VisibleSymbol(reel, travelled, row);
                for (var id = 0; id < 12; id++)
                    Class(state, $"bb_motion{reel}_{row}", "bb-symbol-" + id, (int)visible == id);
            }
            for (var row = 0; row < 3; row++)
            {
                var cell = reel * 3 + row;
                var actual = spin is null ? (BonusSlotSymbol)(cell % 8) : spin.Grid[cell];
                for (var id = 0; id < 12; id++)
                    Class(state, "bb_symbol" + cell, "bb-symbol-" + id, (int)actual == id);
                Class(state, "bb_symbol" + cell, "winning", !moving && spin is not null &&
                    spin.LineWins.Any(w => BonusSlotRules.Paylines[w.Payline].Take(w.MatchCount).Contains(cell)));
                Class(state, "bb_symbol" + cell, "active-line", current is not null &&
                    BonusSlotRules.Paylines[current.Payline].Take(current.MatchCount).Contains(cell));
                Class(state, "bb_symbol" + cell, "expanded", !moving && spin?.State.ExpandingSymbol is { } symbol &&
                    actual == symbol && Enumerable.Range(0, 3).All(r => spin.Grid[reel * 3 + r] == symbol));
            }
        }
    }

    private void RenderBiggestBoy(State state, BiggestBoySpinResult? spin, bool moving)
    {
        var collector = spin is null || spin.State.RulesVersion == BiggestBoyCollectorRules.Version;
        Class(state, "root", "collector-mode", collector);
        var rows = collector ? 3 : 5;
        var lines = collector ? BiggestBoyCollectorRules.Paylines : BiggestBoyRules.Paylines;
        var needsBonusSymbol = !collector && spin is { State.FreeSpinsRemaining: > 0, State.ExpandingSymbol: null };
        Class(state, "root", "needs-bonus-symbol", needsBonusSymbol);
        Text(state, "biggest_win", moving ? "WALZEN LAUFEN …" : spin is null ? "BEREIT ZUM SPIELEN" : spin.Payout > 0 ? "GEWINN DIESES SPINS: " + Points(spin.Payout) : "KEIN GEWINN");
        Text(state, "biggest_spin_win", moving ? "SPIN LÄUFT" : spin is null ? "SPINGEWINN: –" : "SPINGEWINN: " + Points(spin.Payout));
        Text(state, "biggest_total_win", spin is null ? "BONUS / GESAMT: 0 €" : "BONUS / GESAMT: " + Points(spin.State.TotalPayout));
        Text(state, "biggest_bonus", collector && spin is not null
            ? $"{spin.State.FreeSpinsRemaining} Freispiele · gesammelt {Points(spin.State.TotalPayout)} · Wilds {spin.State.Collectors}/12 · ×{BiggestBoyCollectorRules.MultiplierForCollectors(spin.State.Collectors)} · nächster Bonus: {(spin.State.Collectors >= 12 ? "MAX" : (4 - spin.State.Collectors % 4).ToString(System.Globalization.CultureInfo.InvariantCulture) + " Wilds")} "
            : spin is null ? "3/4/5 Gesichter: 10/15/20 Freispiele · Kebap ist Wild" :
            needsBonusSymbol ? $"{spin.State.FreeSpinsRemaining} Freispiele · BONUSSYMBOL WÄHLEN" :
            $"{spin.State.FreeSpinsRemaining} Freispiele · gesammelt {Points(spin.State.TotalPayout)} · {spin.ScatterCount} Scatter · Symbol: {(spin.State.ExpandingSymbol is { } expanding ? BiggestName(expanding) : "–")}");
        Text(state, "detail", moving ? "" : spin is null ? Rules(state.Game) :
            (spin.WinLimitReached ? "HÖCHSTGEWINN 5.000× erreicht · Runde beendet · " : "") +
            (collector ? $"Spingewinn: {Points(spin.Payout)} · Sammlung: {Points(spin.CollectionPayout)} (×{spin.CollectionMultiplier}) · " : "") +
            string.Join(" · ", spin.LineWins.Select(win => $"Linie {win.Payline + 1}: {win.MatchCount}× {BiggestName(win.Symbol)} = {Points(win.Payout)}")));
        for (var reel = 0; reel < 5; reel++)
        {
            var stop = spin is null ? 0 : spin.Stops[reel];
            for (var bit = 0; bit < 7; bit++) Class(state, "biggest_shift" + reel + "_" + bit, "shifted", (stop & (1 << bit)) != 0);
            for (var row = 0; row < rows; row++)
            {
                var cell = reel * rows + row;
                var display = reel * 5 + row;
                var symbol = spin is null ? BiggestBoyRules.ReelStrips[reel][row] : spin.Grid[cell];
                var money = spin is not null && !spin.MoneyValues.IsDefaultOrEmpty && cell < spin.MoneyValues.Length && spin.MoneyValues[cell] > 0 ? Points(spin.MoneyValues[cell]) : "";
                Text(state, "biggest_symbol_money" + display, money);
                Text(state, "biggest_repeat_money" + display, money);
                for (var index = 0; index < 11; index++)
                {
                    Class(state, "biggest_symbol" + display, "biggest-symbol-" + index, (int)symbol == index);
                    Class(state, "biggest_repeat" + display, "biggest-symbol-" + index, (int)symbol == index);
                }
                Class(state, "biggest_symbol" + display, "winning", !moving && spin is not null &&
                    spin.LineWins.Any(win => lines[win.Payline].Take(win.MatchCount).Contains(cell)));
                Class(state, "biggest_symbol" + display, "expanded", !moving && spin is not null && spin.ExpandedReels.Contains(reel));
            }
        }
    }

    private static string BiggestName(BiggestBoySymbol symbol) => symbol switch
    {
        BiggestBoySymbol.Kebap => "DÖNER",
        BiggestBoySymbol.Book => "BIGGEST BOY / WILD",
        BiggestBoySymbol.Beer => "BIER",
        BiggestBoySymbol.Cigarette => "ZIGARETTE",
        BiggestBoySymbol.Butter => "BUTTER",
        BiggestBoySymbol.Shot => "SHOT",
        BiggestBoySymbol.Funnel => "TRINKTRICHTER",
        BiggestBoySymbol.Scatter => "GESICHT / SCATTER",
        _ => symbol.ToString(),
    };

    private void ClearCards(State state)
    {
        foreach (var row in new[] { "dealer", "hand0", "hand1" })
            for (var index = 0; index < 21; index++) RenderCard(state, row + "_card" + index, null, false, false);
        for (var hand = 0; hand < 2; hand++)
        {
            Class(state, "hand" + hand, "present", false);
            Class(state, "hand" + hand, "active-hand", false);
            Text(state, "hand" + hand + "_summary", "");
        }
    }

    private void RenderCards(State state, BlackjackState blackjack, bool moving)
    {
        var settled = blackjack.Status == BlackjackRoundStatus.Settled;
        for (var index = 0; index < 21; index++)
            RenderCard(state, "dealer_card" + index,
                index < blackjack.DealerCards.Length && (settled || index == 0) ? blackjack.DealerCards[index] : null,
                !settled && index == 1, moving);
        for (var handIndex = 0; handIndex < 2; handIndex++)
        {
            var hand = handIndex < blackjack.PlayerHands.Length ? blackjack.PlayerHands[handIndex] : null;
            Class(state, "hand" + handIndex, "present", hand is not null);
            Class(state, "hand" + handIndex, "active-hand", hand is not null && !settled && blackjack.ActiveHandIndex == handIndex);
            Text(state, "hand" + handIndex + "_summary", hand is null ? "" : $"Hand {handIndex + 1} · {hand.BestTotal}{(hand.IsSoft ? " / weich" : "")} · {Points(hand.Stake)}{(hand.IsBust ? " · ÜBER 21" : hand.IsStanding ? " · STEHT" : "")}");
            for (var index = 0; index < 21; index++)
                RenderCard(state, "hand" + handIndex + "_card" + index,
                    hand is not null && index < hand.Cards.Length ? hand.Cards[index] : null, false, moving);
        }
    }

    private void RenderCard(State state, string id, BlackjackCard? card, bool hidden, bool moving)
    {
        // Concealed dealer cards never contribute rank, suit or colour to HUD data.
        var text = hidden ? "?" : card is { } face ? Cards([face]) : "";
        if (!hidden && text.Length > 0) text = text.Insert(text.Length - 1, "\n");
        var changed = !state.Cards.TryGetValue(id, out var previous) || previous != text;
        state.Cards[id] = text;
        Text(state, id, text);
        Class(state, id, "present", hidden || card is not null);
        Class(state, id, "face-down", hidden);
        Class(state, id, "red", !hidden && card is { Suit: BlackjackSuit.Hearts or BlackjackSuit.Diamonds });
        if (!moving || changed)
        {
            Class(state, id, "deal-a", moving && changed && text.Length > 0 && state.Animation % 2 == 1);
            Class(state, id, "deal-b", moving && changed && text.Length > 0 && state.Animation % 2 == 0);
        }
    }

    private static T Read<T>(DurableCasinoRoundSnapshot round) => JsonSerializer.Deserialize<T>(round.StateJson) ?? throw new CasinoRejectedException("Spielstand ungültig.");
    private static string SymbolName(BonusSlotSymbol symbol) => symbol == BonusSlotSymbol.Book ? "BUCH" : symbol.ToString().ToUpperInvariant();
    private static CasinoRoundAction? ParseAction(string action) => action switch
    { "hit" => CasinoRoundAction.Hit, "stand" => CasinoRoundAction.Stand, "double" => CasinoRoundAction.Double, "split" => CasinoRoundAction.Split, "risk" => CasinoRoundAction.Risk, "bank" => CasinoRoundAction.Bank, "free" => CasinoRoundAction.FreeSpin, _ => null };
    private static string Cards(IEnumerable<BlackjackCard> cards) => string.Join("  ", cards.Select(card => (card.Rank == BlackjackRank.Ace ? "A" : card.Rank == BlackjackRank.King ? "K" : card.Rank == BlackjackRank.Queen ? "Q" : card.Rank == BlackjackRank.Jack ? "J" : ((int)card.Rank).ToString(CultureInfo.InvariantCulture)) + (card.Suit switch { BlackjackSuit.Clubs => "♣", BlackjackSuit.Diamonds => "♦", BlackjackSuit.Hearts => "♥", _ => "♠" })));
    private static string Rules(CasinoRoundGame game) => game switch
    {
        CasinoRoundGame.Blackjack => "Blackjack 3:2 · Dealer steht auf Soft 17 · max. 2 Hände · Split-Asse erhalten eine Karte · Double/Split kosten einen zusätzlichen Handeinsatz.",
        CasinoRoundGame.BiggestBoy => "5×3 · 10 Linien · Einsatz/10. Kebap-Wild sammelt im Bonus Bierwerte. 3/4/5 Gesichter: 10/15/20 Freispiele. Je vier Wilds +10 Freispiele und ×2/×3/×10 ab nächstem Spin. Max. drei Verlängerungen. Rundengewinn max. 5.000× Einsatz, danach Bonusende. Auszahlung am Rundenende.",
        CasinoRoundGame.Book => "5 Walzen · 10 Linien · BUCH ist Wild/Scatter · 3+ Bücher: 10 Freispiele · erneute Scatter: +5, max. 30 Freispiele · Einsatz bleibt fix. Auszahlung am Bonusende.",
        _ => "50 % Chance: Zwischenstand verdoppeln oder verlieren · BANK bucht den Zwischenstand zurück · nach 5 Erfolgen BANK wählen.",
    };
    private static string Points(long value) => CasinoMoney.Format(value);
    private void Text(State s, string id, string value) => _hud.SetText(s.Player.Id, s.Hud, "casino_round_" + id, value);
    private void Class(State s, string id, string name, bool value)
    {
        var key = (id, name);
        if (s.Classes.TryGetValue(key, out var previous) && previous == value) return;
        s.Classes[key] = value;
        _hud.SetClass(s.Player.Id, s.Hud, "casino_round_" + id, name, value);
    }
    private bool Current(State s) => !_disposed && _states.TryGetValue(s.Player.Id, out var state) && ReferenceEquals(state, s)
        && _players.TryGet(s.Player.Id, out var player) && player is { IsConnected: true } && player.SessionId == s.Player.SessionId;
    private void Forget(PlayerSnapshot player) { lock (_gate) if (_states.TryGetValue(player.Id, out var state) && state.Player.SessionId == player.SessionId) { _states.Remove(player.Id); _hud.Hide(player.Id, HudId); _hud.Hide(player.Id, ExpandingHudId); } }
    public void Close(PlayerId player) { lock (_gate) { if (_states.TryGetValue(player, out var state)) state.Visible = false; _hud.Hide(player, HudId); _hud.Hide(player, ExpandingHudId); } }
    public void Reset() { lock (_gate) { _states.Clear(); _hud.HideAll(HudId); _hud.HideAll(ExpandingHudId); } }
    public void Dispose() { lock (_gate) { if (_disposed) return; Reset(); _disposed = true; foreach (var owned in _owned) owned.Dispose(); } }
    private sealed class State(PlayerSnapshot player, CasinoRoundGame game)
    {
        public PlayerSnapshot Player { get; } = player;
        public CustomHudId Hud { get; set; } = HudId;
        public CosmeticReelMotion? CosmeticMotion { get; set; }
        public int[] PreviousStops { get; set; } = new int[5];
        public int[] MotionProgress { get; } = [-1, -1, -1, -1, -1];
        public int[] MotionFrames { get; } = new int[5];
        public int WinCycle { get; set; }
        public DateTimeOffset? WinCycleAt { get; set; }
        public CasinoRoundGame Game { get; set; } = game;
        public DurableCasinoRoundSnapshot? Round { get; set; }
        public CasinoBalance Balance { get; set; } = new(0, 0, 0);
        public int Stake { get; set; } = 100;
        public bool Visible { get; set; }
        public bool Pending { get; set; }
        public DurableCasinoRoundOperation? Unconfirmed { get; set; }
        public DateTimeOffset? AnimatingUntil { get; set; }
        public DateTimeOffset? AnimationStartsAt { get; set; }
        public int Animation { get; set; }
        public bool BiggestRules { get; set; }
        public int BookSettles { get; set; }
        public int BookStops { get; set; }
        public bool ReelSoundEnabled { get; set; } = true;
        public int SoundStops { get; set; }
        public Dictionary<string, string> Cards { get; } = [];
        public Dictionary<(string Id, string Name), bool> Classes { get; } = [];
        public string Status { get; set; } = "";
        public bool StatusError { get; set; }
    }
}
