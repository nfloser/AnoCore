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
    private static readonly PermissionId Permission = new("ano.casino.play");
    private static readonly string[] Buttons = ["play", "minus", "plus", "hit", "stand", "double", "split", "risk", "bank", "free", "retry", "close", "back", "refresh", "rules"];
    private readonly ICustomHudService _hud;
    private readonly IPlayerRegistry _players;
    private readonly IPermissionEvaluator _permissions;
    private readonly ICasinoWallet _wallet;
    private readonly ICasinoRounds _rounds;
    private readonly Action<PlayerId> _beforeOpen;
    private readonly Func<PlayerId, CancellationToken, ValueTask> _back;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<PlayerId, State> _states = [];
    private readonly List<IDisposable> _owned = [];
    private bool _disposed;

    public CasinoRoundPresenter(ICustomHudService hud, IPlayerRegistry players, IPermissionEvaluator permissions,
        ICasinoWallet wallet, ICasinoRounds rounds, IAnoEventBus events, Action<PlayerId> beforeOpen,
        Func<PlayerId, CancellationToken, ValueTask> back, TimeProvider? clock = null)
    {
        _hud = hud; _players = players; _permissions = permissions; _wallet = wallet; _rounds = rounds;
        _beforeOpen = beforeOpen; _back = back; _clock = clock ?? TimeProvider.System;
        try
        {
            _owned.Add(hud.Register(new("ano.casino"), new(HudId,
                "panorama/layout/custom_game/anocore/ano_casino_rounds.xml", "casino_round_root",
                Buttons.Select(x => "casino_round_" + x).ToArray(), captureInput: true), ClickAsync));
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
                state.Balance = balance; state.Visible = true; state.Pending = false;
                state.Status = round is { IsComplete: false } ? "Gespeicherte Runde fortsetzen" : "Einsatz wählen und Runde starten";
                _beforeOpen(player); Render(state);
                return _hud.Show(player, HudId);
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
            if (!Current(state) || !state.Visible) return;
            if (!allowed) { Forget(state.Player); return; }
            var action = context.ButtonId.StartsWith("casino_round_", StringComparison.Ordinal) ? context.ButtonId[13..] : "";
            if (!Buttons.Contains(action, StringComparer.Ordinal)) return;
            if (action is "close" or "back") { Close(context.PlayerId); back = action == "back"; closed = true; }
            else
            {
                if (state.Pending || state.AnimatingUntil is not null) return;
                if (state.Unconfirmed is not null && action is not ("retry" or "rules"))
                { state.Status = "Unbestätigte Aktion: BUCHUNG PRÜFEN verwenden."; Render(state); return; }
                if (action == "rules") { state.Status = Rules(state.Game); Render(state); return; }
                if (action is "minus" or "plus")
                {
                    if (state.Round is { IsComplete: false }) return;
                    state.Stake = Math.Clamp(state.Stake + (action == "plus" ? 50 : -50), 50, 500); Render(state); return;
                }
                if (action == "retry") operation = state.Unconfirmed;
                else if (action == "play" && state.Round is not { IsComplete: false })
                    operation = new(Guid.NewGuid(), context.PlayerId, Guid.NewGuid(), state.Game, CasinoRoundAction.Start, state.Stake, 0);
                else if (state.Round is { IsComplete: false } round && ParseAction(action) is { } roundAction)
                    operation = new(Guid.NewGuid(), context.PlayerId, round.Id, round.Game, roundAction, round.Stake, round.Revision);
                if (operation is null && action != "refresh") return;
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
                state.Round = round; state.Balance = balance;
                if (round is not null) state.Game = round.Game;
                if (receipt is not null)
                {
                    state.Unconfirmed = null; state.Animation++;
                    for (var reel = 0; reel < 5; reel++) Class(state, "reel" + reel, "stopped", false);
                    state.AnimatingUntil = _clock.GetUtcNow().AddSeconds(state.Game == CasinoRoundGame.Book ? 3 : .65);
                    state.Status = "Aktion gebucht · Animation läuft";
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
            }
        }
        finally { lock (_gate) { state.Pending = false; if (Current(state) && state.Visible) Render(state); } }
    }

    public void Tick()
    {
        lock (_gate) foreach (var state in _states.Values.ToArray())
        {
            if (!Current(state)) { Forget(state.Player); continue; }
            if (state.Game == CasinoRoundGame.Book && state.AnimatingUntil is { } bookEnd)
                for (var reel = 0; reel < 5; reel++) if (_clock.GetUtcNow() >= bookEnd.AddSeconds(-1 + reel * .2)) Class(state, "reel" + reel, "stopped", true);
            if (state.AnimatingUntil is { } end && _clock.GetUtcNow() >= end)
            { state.AnimatingUntil = null; state.Status = "Gespeichertes Ergebnis"; if (state.Visible) Render(state); }
        }
    }

    private void Render(State state)
    {
        foreach (var game in Enum.GetValues<CasinoRoundGame>()) Class(state, "root", "view-" + game.ToString().ToLowerInvariant(), state.Game == game);
        var active = state.Round is { IsComplete: false };
        var moving = state.AnimatingUntil is not null;
        Class(state, "root", "complete", state.Round is { IsComplete: true });
        Class(state, "root", "active", active); Class(state, "root", "busy", state.Pending || moving);
        Class(state, "root", "uncertain", state.Unconfirmed is not null);
        Class(state, "root", "motion-a", moving && state.Animation % 2 == 1);
        Class(state, "root", "motion-b", moving && state.Animation % 2 == 0);
        Text(state, "title", state.Game switch { CasinoRoundGame.Blackjack => "BLACKJACK", CasinoRoundGame.Book => "BOOK · FREISPIELE", _ => "RISK · DOUBLE OR BANK" });
        Text(state, "account", Points(state.Balance.Available)); Text(state, "bankroll", Points(state.Balance.Allocated));
        Text(state, "stake", Points(active ? state.Round!.Stake : state.Stake)); Text(state, "status", state.Status);
        Text(state, "payout", state.Round is { IsComplete: true } round && !moving ? Points(round.Payout) : "0 AP");
        Class(state, "root", "can-risk", false);
        Class(state, "root", "can-double", false); Class(state, "root", "can-split", false);
        if (state.Round is not { } snapshot) { Text(state, "detail", Rules(state.Game)); return; }
        switch (snapshot.Game)
        {
            case CasinoRoundGame.Risk:
                var risk = Read<RiskRoundState>(snapshot);
                Class(state, "root", "can-risk", !snapshot.IsComplete && risk.Gambles < RiskRules.MaximumGambles && risk.HeldPayout <= RiskRules.MaximumHeldExposure / 2);
                Text(state, "detail", $"Zwischenstand: {Points(risk.HeldPayout)} · Stufe {risk.Gambles}/5 · Chance 50 % · nächste Stufe {Points(risk.HeldPayout * 2L)}"); break;
            case CasinoRoundGame.Blackjack:
                var blackjack = Read<BlackjackState>(snapshot);
                Text(state, "dealer", "Dealer: " + Cards((blackjack.Status == BlackjackRoundStatus.Settled) ? blackjack.DealerCards : blackjack.DealerCards.Take(1)) + ((blackjack.Status == BlackjackRoundStatus.Settled) ? $" ({new BlackjackHand(blackjack.DealerCards, 0, true, false, false).BestTotal})" : " · verdeckt"));
                Text(state, "detail", string.Join("\n", blackjack.PlayerHands.Select((hand, index) => $"{(index == blackjack.ActiveHandIndex ? "▶ " : "")}Hand {index + 1}: {Cards(hand.Cards)} ({hand.BestTotal}) · {Points(hand.Stake)}")));
                if (!(blackjack.Status == BlackjackRoundStatus.Settled))
                {
                    Class(state, "root", "can-double", BlackjackRules.LegalActions(blackjack).Contains(BlackjackAction.Double));
                    Class(state, "root", "can-split", BlackjackRules.LegalActions(blackjack).Contains(BlackjackAction.Split));
                }
                break;
            case CasinoRoundGame.Book:
                var book = Read<BonusSlotSpinResult>(snapshot);
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

    private static T Read<T>(DurableCasinoRoundSnapshot round) => JsonSerializer.Deserialize<T>(round.StateJson) ?? throw new CasinoRejectedException("Spielstand ungültig.");
    private static string SymbolName(BonusSlotSymbol symbol) => symbol == BonusSlotSymbol.Book ? "BUCH" : symbol.ToString().ToUpperInvariant();
    private static CasinoRoundAction? ParseAction(string action) => action switch
    { "hit" => CasinoRoundAction.Hit, "stand" => CasinoRoundAction.Stand, "double" => CasinoRoundAction.Double, "split" => CasinoRoundAction.Split, "risk" => CasinoRoundAction.Risk, "bank" => CasinoRoundAction.Bank, "free" => CasinoRoundAction.FreeSpin, _ => null };
    private static string Cards(IEnumerable<BlackjackCard> cards) => string.Join("  ", cards.Select(card => (card.Rank == BlackjackRank.Ace ? "A" : card.Rank == BlackjackRank.King ? "K" : card.Rank == BlackjackRank.Queen ? "Q" : card.Rank == BlackjackRank.Jack ? "J" : ((int)card.Rank).ToString(CultureInfo.InvariantCulture)) + (card.Suit switch { BlackjackSuit.Clubs => "♣", BlackjackSuit.Diamonds => "♦", BlackjackSuit.Hearts => "♥", _ => "♠" })));
    private static string Rules(CasinoRoundGame game) => game switch
    {
        CasinoRoundGame.Blackjack => "Blackjack 3:2 · Dealer steht auf Soft 17 · max. 2 Hände · Split-Asse erhalten eine Karte · Double/Split kosten einen zusätzlichen Handeinsatz.",
        CasinoRoundGame.Book => "5 Walzen · 10 Linien · BUCH ist Wild/Scatter · 3+ Bücher: 10 Freispiele · erneute Scatter: +5, max. 30 Freispiele · Einsatz bleibt fix. Auszahlung am Bonusende.",
        _ => "50 % Chance: Zwischenstand verdoppeln oder verlieren · BANK bucht den Zwischenstand zurück · nach 5 Erfolgen BANK wählen.",
    };
    private static string Points(long value) => value.ToString("N0", CultureInfo.GetCultureInfo("de-DE")) + " AP";
    private void Text(State s, string id, string value) => _hud.SetText(s.Player.Id, HudId, "casino_round_" + id, value);
    private void Class(State s, string id, string name, bool value) => _hud.SetClass(s.Player.Id, HudId, "casino_round_" + id, name, value);
    private bool Current(State s) => !_disposed && _states.TryGetValue(s.Player.Id, out var state) && ReferenceEquals(state, s)
        && _players.TryGet(s.Player.Id, out var player) && player is { IsConnected: true } && player.SessionId == s.Player.SessionId;
    private void Forget(PlayerSnapshot player) { lock (_gate) if (_states.TryGetValue(player.Id, out var state) && state.Player.SessionId == player.SessionId) { _states.Remove(player.Id); _hud.Hide(player.Id, HudId); } }
    public void Close(PlayerId player) { lock (_gate) { if (_states.TryGetValue(player, out var state)) state.Visible = false; _hud.Hide(player, HudId); } }
    public void Reset() { lock (_gate) { _states.Clear(); _hud.HideAll(HudId); } }
    public void Dispose() { lock (_gate) { if (_disposed) return; Reset(); _disposed = true; foreach (var owned in _owned) owned.Dispose(); } }
    private sealed class State(PlayerSnapshot player, CasinoRoundGame game)
    {
        public PlayerSnapshot Player { get; } = player;
        public CasinoRoundGame Game { get; set; } = game;
        public DurableCasinoRoundSnapshot? Round { get; set; }
        public CasinoBalance Balance { get; set; } = new(0, 0, 0);
        public int Stake { get; set; } = 100;
        public bool Visible { get; set; }
        public bool Pending { get; set; }
        public DurableCasinoRoundOperation? Unconfirmed { get; set; }
        public DateTimeOffset? AnimatingUntil { get; set; }
        public int Animation { get; set; }
        public string Status { get; set; } = "";
    }
}
