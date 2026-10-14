using System.Globalization;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;
namespace AnoCore.Modules.Arcade.Casino;

public sealed class CasinoPresenter : IDisposable
{
    public static readonly CustomHudId HudId = new("ano.casino");
    private static readonly PermissionId Permission = new("ano.casino.play");
    private readonly ICustomHudService _hud;
    private readonly IPlayerRegistry _players;
    private readonly IPermissionEvaluator _permissions;
    private readonly ICasinoWallet _wallet;
    private readonly IAnoCommandRegistry _commands;
    private readonly Action<PlayerId> _beforeOpen;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<PlayerId, State> _states = [];
    private readonly List<IDisposable> _owned = [];
    private bool _disposed;
    private static readonly int[][] WinningLines = [[0, 3, 6], [1, 4, 7], [2, 5, 8], [0, 4, 8], [2, 4, 6]];
    private static readonly HashSet<int> RouletteReds = [1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36];
    private static readonly string[] Buttons = ["close", "home", "menu", "slots", "coinflip", "roulette", "load", "return", "loadminus", "loadplus", "minus", "plus", "play", "head", "tail", "numberminus", "numberplus", "number", "refresh", "history", "rules", "retry", .. Enumerable.Range(37, 12).Select(n => "bet" + n), .. Enumerable.Range(0, 37).Select(n => "r" + n)];
    public CasinoPresenter(ICustomHudService hud, IPlayerRegistry players, IPermissionEvaluator permissions,
        ICasinoWallet wallet, IAnoCommandRegistry commands, IAnoEventBus events, Action<PlayerId> beforeOpen, TimeProvider? clock = null)
    {
        _hud = hud; _players = players; _permissions = permissions; _wallet = wallet; _commands = commands; _beforeOpen = beforeOpen; _clock = clock ?? TimeProvider.System;
        try
        {
            _owned.Add(hud.Register(new("ano.casino"), new(HudId, "panorama/layout/custom_game/anocore/ano_casino.xml", "casino_root", Buttons.Select(x => "casino_" + x).ToArray(), captureInput: true), ClickAsync));
            _owned.Add(commands.Register(new("ano.casino"), new("anocasino", "Open AnoCasino.", Permission), OpenAsync));
            _owned.Add(commands.Register(new("ano.casino"), new("anoap", "Show your profile-linked AP balance.", Permission), async c =>
            {
                if (c.Caller is null) return CommandResult.Fail(CommandFailureReason.Forbidden, "Spieler erforderlich.");
                var b = await wallet.ReadAsync(c.Caller, c.CancellationToken).ConfigureAwait(false);
                return CommandResult.Ok($"[ANO] Profil: {b.Available:N0} AP · Automat: {b.Allocated:N0} AP");
            }));
            _owned.Add(commands.Register(new("ano.casino"), new("anoapcredit", "Console-only audited AP credit."), async c =>
            {
                if (c.Caller is not null) return CommandResult.Fail(CommandFailureReason.Forbidden, "Nur Serverkonsole.");
                if (c.Arguments.Count != 2 || !ulong.TryParse(c.Arguments[0], out var id) || !int.TryParse(c.Arguments[1], out var amount) || amount is < 1 or > 1000000)
                    return CommandResult.Fail(CommandFailureReason.InvalidInput, "anoapcredit <SteamID64> <1–1000000 AP>");
                try { await wallet.ApplyAsync(new(Guid.NewGuid(), new(id), CasinoAction.Credit, amount, null), c.CancellationToken).ConfigureAwait(false); }
                catch (ArgumentException) { return CommandResult.Fail(CommandFailureReason.InvalidInput, "Ungültige SteamID/AP."); }
                return CommandResult.Ok("[ANO] AP-Gutschrift protokolliert. Anzeige mit Aktualisieren neu laden.");
            }));
            _owned.Add(events.Subscribe<PlayerDisconnectedEvent>((e, _) => { ForgetSession(e.Player); return ValueTask.CompletedTask; }));
            _owned.Add(events.Subscribe<PlayerReconnectedEvent>((e, _) => { ForgetSession(e.Previous); return ValueTask.CompletedTask; }));
        }
        catch { foreach (var x in _owned) x.Dispose(); throw; }
    }
    private async ValueTask<CommandResult> OpenAsync(CommandContext c)
    {
        State state;
        lock (_gate)
        {
            if (_disposed || c.Caller is null || !_players.TryGet(c.Caller, out var p) || p is not { IsConnected: true }) return CommandResult.Fail(CommandFailureReason.Forbidden, "Spieler erforderlich.");
            if (!_states.TryGetValue(p.Id, out state!) || state.Player.SessionId != p.SessionId) _states[p.Id] = state = new(p);
            if (state.Pending) return CommandResult.Fail(CommandFailureReason.Forbidden, "Buchung läuft bereits.");
            state.Pending = true;
        }
        try
        {
            var balance = await _wallet.ReadAsync(state.Player.Id, c.CancellationToken).ConfigureAwait(false);
            var history = await _wallet.HistoryAsync(state.Player.Id, c.CancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (!Current(state)) return CommandResult.Fail(CommandFailureReason.Forbidden, "Sitzung beendet.");
                state.Balance = balance; state.Pending = false;
                if (state.Started is null && history.FirstOrDefault(x => x.Outcome is not null) is { } last) { state.Outcome = last.Outcome; state.Win = last.Outcome!.Payout; state.Status = "Letztes gebuchtes Spiel: " + last.Outcome.Summary; SetOutcome(state, last.Outcome); }
                for (var i = 0; i < 3; i++) Class(state, "reel" + i, "rolling", state.Started is not null && state.Game == CasinoGame.Slots); _beforeOpen(state.Player.Id); state.Visible = true; Render(state);
                return _hud.Show(state.Player.Id, HudId) ? CommandResult.Ok("[ANO] Casino geöffnet.") : CommandResult.Fail(CommandFailureReason.Forbidden, "HUD nicht verfügbar.");
            }
        }
        finally { lock (_gate) state.Pending = false; }
    }
    private async ValueTask ClickAsync(CustomHudClickContext c)
    {
        State state;
        lock (_gate) if (_disposed || !_states.TryGetValue(c.PlayerId, out state!) || !state.Visible) return;
        var allowed = await _permissions.HasPermissionAsync(c.PlayerId, Permission, c.CancellationToken).ConfigureAwait(false);
        CasinoOperation? operation = null; var history = false; var refresh = false; var menu = false;
        lock (_gate)
        {
            if (!Current(state) || !state.Visible) return;
            if (!allowed) { ForgetSession(state.Player); return; }
            var action = c.ButtonId.StartsWith("casino_", StringComparison.Ordinal) ? c.ButtonId[7..] : "";
            if (!Buttons.Contains(action, StringComparer.Ordinal)) return;
            if (action is "close" or "menu") { state.Visible = false; _hud.Hide(c.PlayerId, HudId); menu = action == "menu"; }
            else
            {
                if (state.Pending || state.Started is not null) return;
                if (state.Unconfirmed is not null && action is not ("retry" or "refresh" or "history")) { state.Status = "Unbestätigte Buchung: BUCHUNG PRÜFEN verwenden. Es wird nicht erneut gewürfelt."; Render(state); return; }
                switch (action)
                {
                    case "home": state.Game = null; break;
                    case "slots": state.Game = CasinoGame.Slots; break;
                    case "coinflip": state.Game = CasinoGame.Coinflip; break;
                    case "roulette": state.Game = CasinoGame.Roulette; break;
                    case "minus": state.Stake = Math.Max(50, state.Stake - 50); break;
                    case "plus": state.Stake = Math.Min(500, state.Stake + 50); break;
                    case "loadminus": state.Load = Math.Max(50, state.Load - 50); break;
                    case "loadplus": state.Load = Math.Min(10000, state.Load + 50); break;
                    case "head": state.Coin = 0; break;
                    case "tail": state.Coin = 1; break;
                    case "numberminus": state.Number = Math.Max(0, state.Number - 1); state.Bet = state.Number; break;
                    case "numberplus": state.Number = Math.Min(36, state.Number + 1); state.Bet = state.Number; break;
                    case "number": state.Bet = state.Number; break;
                    case "load": operation = new(Guid.NewGuid(), c.PlayerId, CasinoAction.Load, state.Load, null); break;
                    case "return": operation = new(Guid.NewGuid(), c.PlayerId, CasinoAction.Return, 0, null); break;
                    case "play" when state.Game is { } game:
                        operation = new(Guid.NewGuid(), c.PlayerId, CasinoAction.Play, 0, new(game, state.Stake, game == CasinoGame.Coinflip ? state.Coin : state.Bet)); break;
                    case "retry": operation = state.Unconfirmed; break;
                    case "refresh": refresh = true; break;
                    case "history": history = true; break;
                    case "rules": state.Status = "Slots: 5 Linien, Einsatz/5, 12/22/36/55/100/150/300/1000×. Coinflip 2×. Roulette Zahl 36×, Dutzend/Spalte 3×, andere 2×. Auszahlung inklusive Einsatz."; break;
                    default:
                        if (action.StartsWith("bet", StringComparison.Ordinal) && int.TryParse(action[3..], out var bet)) state.Bet = bet;
                        if (action.StartsWith("r", StringComparison.Ordinal) && int.TryParse(action[1..], out var number) && number is >= 0 and <= 36) { state.Number = number; state.Bet = number; }
                        break;
                }
                if (operation is not null || refresh || history) state.Pending = true;
                if (operation is not null) state.Unconfirmed = operation;
                Render(state);
            }
        }
        if (menu) { await _commands.ExecuteAsync("!anomenu", c.PlayerId, c.CancellationToken).ConfigureAwait(false); return; }
        if (operation is null && !refresh && !history) return;
        try
        {
            var receipt = operation is null ? null : await _wallet.ApplyAsync(operation, c.CancellationToken).ConfigureAwait(false);
            var balance = await _wallet.ReadAsync(c.PlayerId, c.CancellationToken).ConfigureAwait(false);
            var entries = history ? await _wallet.HistoryAsync(c.PlayerId, c.CancellationToken).ConfigureAwait(false) : null;
            lock (_gate)
            {
                if (!Current(state)) return;
                state.Balance = balance;
                if (receipt is not null) state.Unconfirmed = null;
                if (receipt?.Outcome is { } outcome)
                {
                    ClearAnimationClasses(state); state.Outcome = outcome; state.Win = 0; state.Started = _clock.GetUtcNow(); state.Spin++; state.Status = "Ergebnis gebucht · Animation läuft";
                    SetOutcome(state, outcome);
                    for (var i = 0; i < 3; i++)
                    {
                        Class(state, "reel" + i, "roll-a", state.Spin % 2 == 1); Class(state, "reel" + i, "roll-b", state.Spin % 2 == 0);
                        Class(state, "reel" + i, "rolling", outcome.Game == CasinoGame.Slots);
                    }
                    Class(state, "coin", "flipping", outcome.Game == CasinoGame.Coinflip);
                    Class(state, "wheel", "spinning", outcome.Game == CasinoGame.Roulette);
                    Class(state, "pointer", "spinning", outcome.Game == CasinoGame.Roulette);
                }
                else state.Status = entries is null ? "AP-Anzeige aktualisiert" : string.Join(" | ", entries.Where(x => x.Outcome is not null).Take(5).Select(x => x.Outcome!.Summary));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate) if (Current(state))
            {
                if (ex is CasinoRejectedException or ArgumentException) state.Unconfirmed = null;
                state.Status = ex is CasinoRejectedException ? ex.Message : "Buchung unbestätigt. BUCHUNG PRÜFEN verwenden; keine neue Spielanfrage senden.";
            }
        }
        finally { lock (_gate) { state.Pending = false; if (Current(state) && state.Visible) Render(state); } }
    }
    public void Tick()
    {
        lock (_gate) foreach (var state in _states.Values.ToArray())
        {
            if (!Current(state)) { ForgetSession(state.Player); continue; }
            if (state.Started is not { } start) continue;
            var elapsed = _clock.GetUtcNow() - start;
            if (state.Outcome?.Game == CasinoGame.Slots)
            {
                for (var i = 0; i < 3; i++)
                {
                    if ((state.ReelStops & (1 << i)) != 0 || elapsed.TotalSeconds < 2.6 + i * .5) continue;
                    state.ReelStops |= 1 << i;
                    Class(state, "reel" + i, "rolling", false);
                    Class(state, "reel" + i, "settled", true);
                }
            }
            if (!state.LandingApplied && elapsed.TotalSeconds >= 3.6 && state.Outcome is { } settled)
            {
                state.LandingApplied = true;
                Class(state, "coin", "flipping", false);
                Class(state, "coin", settled.Number == 0 ? "land-heads" : "land-tails", settled.Game == CasinoGame.Coinflip);
                Class(state, "wheel", "spinning", false);
                Class(state, "pointer", "spinning", false);
                var color = settled.Number == 0 ? "land-green" : RouletteReds.Contains(settled.Number) ? "land-red" : "land-black";
                Class(state, "wheel", color, settled.Game == CasinoGame.Roulette);
                Class(state, "pointer", color, settled.Game == CasinoGame.Roulette);
            }
            if (elapsed.TotalSeconds < 4) continue;
            if (!state.LinesRevealed && state.Outcome is { Game: CasinoGame.Slots } slots)
            {
                for (var line = 0; line < WinningLines.Length; line++)
                {
                    var cells = WinningLines[line];
                    Class(state, "line" + line, "revealed", true);
                    Class(state, "line" + line, "active", slots.Symbols[cells[0]] == slots.Symbols[cells[1]] && slots.Symbols[cells[1]] == slots.Symbols[cells[2]]);
                }
                state.LinesRevealed = true;
            }
            state.Started = null; state.Win = state.Outcome!.Payout; state.Status = state.Outcome.Summary; if (state.Visible) Render(state);
        }
    }
    private void SetOutcome(State s, CasinoOutcome o)
    {
        if (o.Game == CasinoGame.Slots) for (var i = 0; i < 9; i++) for (var symbol = 0; symbol < CasinoRules.Symbols.Length; symbol++) Class(s, "symbol" + i, "symbol-" + CasinoRules.Symbols[symbol], o.Symbols[i] == symbol);
        Text(s, "result", o.Game == CasinoGame.Coinflip ? (o.Number == 0 ? "KOPF" : "ZAHL") : "");
        Text(s, "wheelresult", o.Game == CasinoGame.Roulette ? o.Number.ToString(CultureInfo.InvariantCulture) : "");
    }
    private void ClearAnimationClasses(State s)
    {
        s.ReelStops = 0; s.LandingApplied = false; s.LinesRevealed = false;
        for (var i = 0; i < 3; i++) { Class(s, "reel" + i, "rolling", false); Class(s, "reel" + i, "settled", false); }
        for (var line = 0; line < WinningLines.Length; line++) { Class(s, "line" + line, "revealed", false); Class(s, "line" + line, "active", false); }
        foreach (var name in new[] { "flipping", "land-heads", "land-tails" }) Class(s, "coin", name, false);
        foreach (var name in new[] { "spinning", "land-red", "land-black", "land-green" }) { Class(s, "wheel", name, false); Class(s, "pointer", name, false); }
    }
    private void Render(State s)
    {
        foreach (var g in Enum.GetValues<CasinoGame>()) Class(s, "root", "view-" + g.ToString().ToLowerInvariant(), s.Game == g);
        Class(s, "root", "home", s.Game is null); Class(s, "root", "busy", s.Pending || s.Started is not null);
        Class(s, "result", "moving-a", s.Started is not null && s.Game == CasinoGame.Coinflip && s.Spin % 2 == 1); Class(s, "result", "moving-b", s.Started is not null && s.Game == CasinoGame.Coinflip && s.Spin % 2 == 0);
        Text(s, "profile", s.Player.Name); Text(s, "account", Points(s.Balance.Available)); Text(s, "bank", Points(s.Balance.Allocated)); Text(s, "stake", Points(s.Stake)); Text(s, "loadamount", Points(s.Load)); Text(s, "win", Points(s.Win)); Text(s, "numbervalue", s.Number.ToString(CultureInfo.InvariantCulture));
        Text(s, "choice", s.Game == CasinoGame.Coinflip ? (s.Coin == 0 ? "Kopf ausgewählt" : "Zahl ausgewählt") : s.Game == CasinoGame.Roulette ? "Wette: " + BetLabel(s.Bet) : "5 Gewinnlinien · Einsatz aufgeteilt"); Text(s, "status", s.Status);
    }
    private static string BetLabel(int n) => n <= 36 ? "Zahl " + n : new[] { "Rot", "Schwarz", "Gerade", "Ungerade", "1–18", "19–36", "1. Dutzend", "2. Dutzend", "3. Dutzend", "1. Spalte", "2. Spalte", "3. Spalte" }[n - 37];
    private static string Points(long n) => n.ToString("N0", CultureInfo.GetCultureInfo("de-DE")) + " AP";
    private void Text(State s, string id, string value) => _hud.SetText(s.Player.Id, HudId, "casino_" + id, value);
    private void Class(State s, string id, string name, bool value) => _hud.SetClass(s.Player.Id, HudId, "casino_" + id, name, value);
    private bool Current(State s) => !_disposed && _states.TryGetValue(s.Player.Id, out var current) && ReferenceEquals(s, current) && _players.TryGet(s.Player.Id, out var p) && p is { IsConnected: true } && p.SessionId == s.Player.SessionId;
    private void ForgetSession(PlayerSnapshot p) { lock (_gate) if (_states.TryGetValue(p.Id, out var s) && s.Player.SessionId == p.SessionId) { _states.Remove(p.Id); _hud.Hide(p.Id, HudId); } }
    public void Reset() { lock (_gate) { _hud.HideAll(HudId); _states.Clear(); } }
    public void Dispose() { lock (_gate) { if (_disposed) return; Reset(); _disposed = true; foreach (var x in _owned) x.Dispose(); } }
    private sealed class State(PlayerSnapshot player)
    {
        public PlayerSnapshot Player { get; } = player;
        public CasinoBalance Balance { get; set; } = new(0, 0, 0);
        public CasinoGame? Game { get; set; }
        public bool Visible { get; set; }
        public bool Pending { get; set; }
        public int Stake { get; set; } = 100;
        public int Load { get; set; } = 500;
        public int Coin { get; set; }
        public int Number { get; set; }
        public int Bet { get; set; } = 37;
        public int Spin { get; set; }
        public int ReelStops { get; set; }
        public bool LandingApplied { get; set; }
        public bool LinesRevealed { get; set; }
        public long Win { get; set; }
        public DateTimeOffset? Started { get; set; }
        public CasinoOutcome? Outcome { get; set; }
        public CasinoOperation? Unconfirmed { get; set; }
        public string Status { get; set; } = "AP-Konto laden oder Guthaben vom Profil auf den Automaten buchen.";
    }
}
