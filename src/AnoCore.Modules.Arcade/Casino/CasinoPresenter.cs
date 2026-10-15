using System.Globalization;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Modules.Arcade.Casino;

/// <summary>
/// Presents AnoCasino as several small Panorama HUDs instead of one monolithic layout.
/// Wallet mutations and outcomes remain server-authoritative; HUDs only project state.
/// </summary>
public sealed class CasinoPresenter : IDisposable
{
    public static readonly CustomHudId LobbyHudId = new("ano.casino.lobby");
    public static readonly CustomHudId SlotsHudId = new("ano.casino.slots");
    public static readonly CustomHudId CoinflipHudId = new("ano.casino.coinflip");
    public static readonly CustomHudId RouletteHudId = new("ano.casino.roulette");

    // Compatibility alias for callers that only need the entry HUD.
    public static readonly CustomHudId HudId = LobbyHudId;

    private static readonly PermissionId Permission = new("ano.casino.play");
    private static readonly int[][] WinningLines = [[0, 3, 6], [1, 4, 7], [2, 5, 8], [0, 4, 8], [2, 4, 6]];
    private static readonly HashSet<int> RouletteReds = [1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36];

    private static readonly string[] LobbyButtons =
    [
        "casino_lobby_close", "casino_lobby_menu",
        "casino_lobby_slots", "casino_lobby_coinflip", "casino_lobby_roulette",
        "casino_lobby_blackjack", "casino_lobby_book", "casino_lobby_risk", "casino_lobby_biggestboy",
        "casino_lobby_load", "casino_lobby_return", "casino_lobby_loadminus",
        "casino_lobby_loadplus", "casino_lobby_refresh", "casino_lobby_history"
    ];

    private static readonly string[] SlotsButtons =
    [
        "casino_slots_close", "casino_slots_back", "casino_slots_minus", "casino_slots_plus",
        "casino_slots_play", "casino_slots_refresh", "casino_slots_rules", "casino_slots_retry"
    ];

    private static readonly string[] CoinflipButtons =
    [
        "casino_coinflip_close", "casino_coinflip_back", "casino_coinflip_minus", "casino_coinflip_plus",
        "casino_coinflip_head", "casino_coinflip_tail", "casino_coinflip_play",
        "casino_coinflip_refresh", "casino_coinflip_rules", "casino_coinflip_retry"
    ];

    private static readonly string[] RouletteButtons =
    [
        "casino_roulette_close", "casino_roulette_back", "casino_roulette_minus", "casino_roulette_plus",
        "casino_roulette_numberminus", "casino_roulette_numberplus", "casino_roulette_number",
        "casino_roulette_play", "casino_roulette_refresh", "casino_roulette_rules", "casino_roulette_retry",
        .. Enumerable.Range(37, 12).Select(n => "casino_roulette_bet" + n),
        .. Enumerable.Range(0, 37).Select(n => "casino_roulette_r" + n)
    ];

    private readonly ICustomHudService _hud;
    private readonly IPlayerRegistry _players;
    private readonly IPermissionEvaluator _permissions;
    private readonly ICasinoWallet _wallet;
    private readonly CasinoStakeLimits _stakeLimits;
    private readonly IAnoCommandRegistry _commands;
    private readonly Action<PlayerId> _beforeOpen;
    private readonly TimeProvider _clock;
    private readonly Func<PlayerId, CasinoRoundGame, CancellationToken, ValueTask<bool>>? _openRound;
    private readonly Action<Exception>? _reportError;
    private readonly object _gate = new();
    private readonly Dictionary<PlayerId, State> _states = [];
    private readonly List<IDisposable> _owned = [];
    private bool _disposed;

    public CasinoPresenter(
        ICustomHudService hud,
        IPlayerRegistry players,
        IPermissionEvaluator permissions,
        ICasinoWallet wallet,
        IAnoCommandRegistry commands,
        IAnoEventBus events,
        Action<PlayerId> beforeOpen,
        TimeProvider? clock = null,
        Func<PlayerId, CasinoRoundGame, CancellationToken, ValueTask<bool>>? openRound = null,
        Action<Exception>? reportError = null,
        CasinoStakeLimits? stakeLimits = null)
    {
        _hud = hud;
        _players = players;
        _permissions = permissions;
        _wallet = wallet;
        _stakeLimits = (stakeLimits ?? new()).Validate();
        _commands = commands;
        _beforeOpen = beforeOpen;
        _clock = clock ?? TimeProvider.System;
        _openRound = openRound;
        _reportError = reportError;

        try
        {
            _owned.Add(hud.Register(
                new("ano.casino"),
                new(LobbyHudId, "panorama/layout/custom_game/anocore/ano_casino_lobby.xml",
                    "casino_lobby_root", LobbyButtons, captureInput: true),
                ClickAsync));

            _owned.Add(hud.Register(
                new("ano.casino"),
                new(SlotsHudId, "panorama/layout/custom_game/anocore/ano_casino_slots.xml",
                    "casino_slots_root", SlotsButtons, captureInput: true),
                ClickAsync));

            _owned.Add(hud.Register(
                new("ano.casino"),
                new(CoinflipHudId, "panorama/layout/custom_game/anocore/ano_casino_coinflip.xml",
                    "casino_coinflip_root", CoinflipButtons, captureInput: true),
                ClickAsync));

            _owned.Add(hud.Register(
                new("ano.casino"),
                new(RouletteHudId, "panorama/layout/custom_game/anocore/ano_casino_roulette.xml",
                    "casino_roulette_root", RouletteButtons, captureInput: true),
                ClickAsync));

            _owned.Add(commands.Register(
                new("ano.casino"),
                new("anocasino", "Open AnoCasino.", Permission),
                OpenAsync));

            _owned.Add(commands.Register(
                new("ano.casino"),
                new("anoap", "Show your profile-linked virtual balance.", Permission),
                async c =>
                {
                    if (c.Caller is null)
                        return CommandResult.Fail(CommandFailureReason.Forbidden, "Spieler erforderlich.");
                    var balance = await wallet.ReadAsync(c.Caller, c.CancellationToken).ConfigureAwait(false);
                    return CommandResult.Ok($"[ANO] Profil: {CasinoMoney.Format(balance.Available)} · Automat: {CasinoMoney.Format(balance.Allocated)}");
                }));

            _owned.Add(commands.Register(
                new("ano.casino"),
                new("anoapcredit", "Console-only audited virtual credit."),
                async c =>
                {
                    if (c.Caller is not null)
                        return CommandResult.Fail(CommandFailureReason.Forbidden, "Nur Serverkonsole.");
                    if (c.Arguments.Count != 2
                        || !ulong.TryParse(c.Arguments[0], out var id)
                        || !int.TryParse(c.Arguments[1], out var amount)
                        || amount is < 1 or > 1000000)
                        return CommandResult.Fail(CommandFailureReason.InvalidInput,
                            "anoapcredit <SteamID64> <1–1000000 €>");
                    try
                    {
                        await wallet.ApplyAsync(
                            new(Guid.NewGuid(), new(id), CasinoAction.Credit, amount, null),
                            c.CancellationToken).ConfigureAwait(false);
                    }
                    catch (ArgumentException)
                    {
                        return CommandResult.Fail(CommandFailureReason.InvalidInput, "Ungültige SteamID/Betrag.");
                    }
                    catch (OperationCanceledException) when (c.CancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _reportError?.Invoke(exception);
                        return CommandResult.Fail(CommandFailureReason.HandlerFailed,
                            "[ANO] Casino-Speicher nicht verfügbar. Serverlog prüfen.");
                    }

                    return CommandResult.Ok("[ANO] Virtuelle Gutschrift protokolliert. Anzeige mit Aktualisieren neu laden.");
                }));

            _owned.Add(events.Subscribe<PlayerDisconnectedEvent>((e, _) =>
            {
                ForgetSession(e.Player);
                return ValueTask.CompletedTask;
            }));
            _owned.Add(events.Subscribe<PlayerReconnectedEvent>((e, _) =>
            {
                ForgetSession(e.Previous);
                return ValueTask.CompletedTask;
            }));
        }
        catch
        {
            foreach (var owned in _owned)
                owned.Dispose();
            throw;
        }
    }

    private async ValueTask<CommandResult> OpenAsync(CommandContext context)
    {
        State state;
        lock (_gate)
        {
            if (_disposed || context.Caller is null
                || !_players.TryGet(context.Caller, out var player)
                || player is not { IsConnected: true })
                return CommandResult.Fail(CommandFailureReason.Forbidden, "Spieler erforderlich.");

            if (!_states.TryGetValue(player.Id, out state!) || state.Player.SessionId != player.SessionId)
                _states[player.Id] = state = new(player);

            if (state.Pending)
                return CommandResult.Fail(CommandFailureReason.Forbidden, "Buchung läuft bereits.");

            state.Pending = true;
        }

        try
        {
            var balance = await _wallet.ReadAsync(state.Player.Id, context.CancellationToken).ConfigureAwait(false);
            var history = await _wallet.HistoryAsync(state.Player.Id, context.CancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                if (!Current(state))
                    return CommandResult.Fail(CommandFailureReason.Forbidden, "Sitzung beendet.");

                state.Balance = balance;
                state.Pending = false;
                state.Game = null;
                state.Visible = true;
                if (history.FirstOrDefault(x => x.Outcome is not null) is { } last)
                {
                    state.Outcome = last.Outcome;
                    state.Win = last.Outcome!.Payout;
                    state.Status = "Letztes gebuchtes Spiel: " + last.Outcome.Summary;
                }

                _beforeOpen(state.Player.Id);
                HideAllViews(state.Player.Id);
                RenderLobby(state);
                return _hud.Show(state.Player.Id, LobbyHudId)
                    ? CommandResult.Ok("[ANO] Casino geöffnet.")
                    : CommandResult.Fail(CommandFailureReason.Forbidden, "HUD nicht verfügbar.");
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _reportError?.Invoke(exception);
            return CommandResult.Fail(CommandFailureReason.HandlerFailed,
                "[ANO] Casino-Speicher nicht verfügbar. Serverlog prüfen.");
        }
        finally
        {
            lock (_gate)
                state.Pending = false;
        }
    }

    private async ValueTask ClickAsync(CustomHudClickContext context)
    {
        State state;
        lock (_gate)
        {
            if (_disposed || !_states.TryGetValue(context.PlayerId, out state!) || !state.Visible)
                return;
        }

        var allowed = await _permissions.HasPermissionAsync(
            context.PlayerId, Permission, context.CancellationToken).ConfigureAwait(false);

        CasinoOperation? operation = null;
        CasinoRoundGame? roundGame = null;
        var refresh = false;
        var history = false;
        var openMenu = false;

        lock (_gate)
        {
            if (!Current(state) || !state.Visible)
                return;
            if (!allowed)
            {
                ForgetSession(state.Player);
                return;
            }

            var action = ActionFrom(context);
            if (action.Length == 0)
                return;

            if (action == "close")
            {
                state.Visible = false;
                HideAllViews(context.PlayerId);
                return;
            }

            if (action == "back")
            {
                state.Game = null;
                HideAllViews(context.PlayerId);
                RenderLobby(state);
                _hud.Show(context.PlayerId, LobbyHudId);
                return;
            }

            if (action == "menu")
            {
                state.Visible = false;
                HideAllViews(context.PlayerId);
                openMenu = true;
            }
            else if (state.Pending || state.Started is not null)
            {
                return;
            }
            else if (state.Unconfirmed is not null && action is not ("retry" or "refresh" or "history"))
            {
                state.Status = "Unbestätigte Buchung: BUCHUNG PRÜFEN verwenden. Es wird nicht erneut gewürfelt.";
                RenderActive(state);
                return;
            }
            else
            {
                switch (action)
                {
                    case "slots":
                        SwitchGame(state, CasinoGame.Slots);
                        return;
                    case "coinflip":
                        SwitchGame(state, CasinoGame.Coinflip);
                        return;
                    case "roulette":
                        SwitchGame(state, CasinoGame.Roulette);
                        return;
                    case "blackjack":
                        roundGame = CasinoRoundGame.Blackjack;
                        break;
                    case "risk":
                        roundGame = CasinoRoundGame.Risk;
                        break;
                    case "biggestboy":
                        roundGame = CasinoRoundGame.BiggestBoy;
                        break;
                    case "book":
                        roundGame = CasinoRoundGame.Book;
                        break;
                    case "minus":
                        state.Stake = Math.Max(50, state.Stake - 50);
                        break;
                    case "plus":
                        state.Stake = Math.Min(_stakeLimits.For(state.Game ?? CasinoGame.Slots), state.Stake + 50);
                        break;
                    case "loadminus":
                        state.Load = Math.Max(50, state.Load - 50);
                        break;
                    case "loadplus":
                        state.Load = Math.Min(10000, state.Load + 50);
                        break;
                    case "head":
                        state.Coin = 0;
                        break;
                    case "tail":
                        state.Coin = 1;
                        break;
                    case "numberminus":
                        state.Number = Math.Max(0, state.Number - 1);
                        state.Bet = state.Number;
                        break;
                    case "numberplus":
                        state.Number = Math.Min(36, state.Number + 1);
                        state.Bet = state.Number;
                        break;
                    case "number":
                        state.Bet = state.Number;
                        break;
                    case "load":
                        operation = new(Guid.NewGuid(), context.PlayerId, CasinoAction.Load, state.Load, null);
                        break;
                    case "return":
                        operation = new(Guid.NewGuid(), context.PlayerId, CasinoAction.Return, 0, null);
                        break;
                    case "play" when state.Game is { } game:
                        operation = new(Guid.NewGuid(), context.PlayerId, CasinoAction.Play, 0,
                            new(game, state.Stake, game == CasinoGame.Coinflip ? state.Coin : state.Bet));
                        break;
                    case "retry":
                        operation = state.Unconfirmed;
                        break;
                    case "refresh":
                        refresh = true;
                        break;
                    case "history":
                        history = true;
                        break;
                    case "rules":
                        state.Status = Rules(state.Game);
                        break;
                    default:
                        if (action.StartsWith("bet", StringComparison.Ordinal)
                            && int.TryParse(action[3..], out var bet))
                            state.Bet = bet;
                        if (action.StartsWith("r", StringComparison.Ordinal)
                            && int.TryParse(action[1..], out var number)
                            && number is >= 0 and <= 36)
                        {
                            state.Number = number;
                            state.Bet = number;
                        }
                        break;
                }

                if (operation is not null || refresh || history)
                    state.Pending = true;
                if (operation is not null)
                    state.Unconfirmed = operation;
                RenderActive(state);
            }
        }

        if (openMenu)
        {
            await _commands.ExecuteAsync("!anomenu", context.PlayerId, context.CancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (roundGame is { } selected && _openRound is not null)
        {
            var opened = false;
            try
            {
                opened = await _openRound(context.PlayerId, selected, context.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _reportError?.Invoke(ex);
            }

            if (!opened)
            {
                lock (_gate)
                {
                    if (Current(state))
                    {
                        state.Status = "Spiel konnte nicht geladen werden. Bitte aktualisieren.";
                        state.Game = null;
                        state.Visible = true;
                        HideAllViews(context.PlayerId);
                        RenderLobby(state);
                        _hud.Show(context.PlayerId, LobbyHudId);
                    }
                }
            }
            return;
        }

        if (operation is null && !refresh && !history)
            return;

        try
        {
            var receipt = operation is null
                ? null
                : await _wallet.ApplyAsync(operation, context.CancellationToken).ConfigureAwait(false);
            var balance = await _wallet.ReadAsync(context.PlayerId, context.CancellationToken).ConfigureAwait(false);
            var entries = history
                ? await _wallet.HistoryAsync(context.PlayerId, context.CancellationToken).ConfigureAwait(false)
                : null;

            lock (_gate)
            {
                if (!Current(state))
                    return;

                state.Balance = balance;
                if (receipt is not null)
                    state.Unconfirmed = null;

                if (receipt?.Outcome is { } outcome)
                {
                    ClearAnimationClasses(state);
                    state.Outcome = outcome;
                    state.Win = 0;
                    state.Started = _clock.GetUtcNow();
                    state.Spin++;
                    state.Status = "Ergebnis gebucht · Animation läuft";
                    SetOutcome(state, outcome);

                    if (outcome.Game == CasinoGame.Slots)
                    {
                        for (var i = 0; i < 3; i++)
                        {
                            SlotsClass(state, "reel" + i, "roll-a", state.Spin % 2 == 1);
                            SlotsClass(state, "reel" + i, "roll-b", state.Spin % 2 == 0);
                            SlotsClass(state, "reel" + i, "rolling", true);
                        }
                    }
                    else if (outcome.Game == CasinoGame.Coinflip)
                    {
                        CoinClass(state, "coin", "toss-b", state.Spin % 2 == 0);
                        CoinClass(state, "coin", "flipping", true);
                    }
                    else if (outcome.Game == CasinoGame.Roulette)
                    {
                        RouletteClass(state, "wheel", "turn-b", state.Spin % 2 == 0);
                        for (var number = 0; number <= 36; number++)
                            RouletteClass(state, "wheel", "target-n" + number, outcome.Number == number);
                        RouletteClass(state, "wheel", "spinning", true);
                        RouletteClass(state, "pointer", "spinning", true);
                    }
                }
                else
                {
                    state.Status = entries is null
                        ? "Guthabenanzeige aktualisiert"
                        : string.Join(" | ", entries.Where(x => x.Outcome is not null)
                            .Take(5).Select(x => x.Outcome!.Summary));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate)
            {
                if (Current(state))
                {
                    if (ex is CasinoRejectedException or ArgumentException)
                        state.Unconfirmed = null;
                    state.Status = ex is CasinoRejectedException
                        ? ex.Message
                        : "Buchung unbestätigt. BUCHUNG PRÜFEN verwenden; keine neue Spielanfrage senden.";
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                state.Pending = false;
                if (Current(state) && state.Visible)
                    RenderActive(state);
            }
        }
    }

    public void Tick()
    {
        lock (_gate)
        {
            foreach (var state in _states.Values.ToArray())
            {
                if (!Current(state))
                {
                    ForgetSession(state.Player);
                    continue;
                }

                if (state.Started is not { } start)
                    continue;

                var elapsed = _clock.GetUtcNow() - start;
                if (state.Outcome?.Game == CasinoGame.Slots)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        if ((state.ReelSettles & (1 << i)) == 0
                            && elapsed >= TimeSpan.FromMilliseconds(2150 + i * 500)
                            && elapsed < TimeSpan.FromMilliseconds(2600 + i * 500))
                        {
                            state.ReelSettles |= 1 << i;
                            SlotsClass(state, "reel" + i, "rolling", false);
                            SlotsClass(state, "reel" + i, "settling", true);
                        }
                        if ((state.ReelStops & (1 << i)) != 0
                            || elapsed < TimeSpan.FromMilliseconds(2600 + i * 500))
                            continue;
                        state.ReelStops |= 1 << i;
                        SlotsClass(state, "reel" + i, "rolling", false);
                        SlotsClass(state, "reel" + i, "settling", false);
                        SlotsClass(state, "reel" + i, "settled", true);
                    }
                }

                if (!state.LandingApplied
                    && elapsed >= TimeSpan.FromMilliseconds(3600)
                    && state.Outcome is { } settled)
                {
                    state.LandingApplied = true;
                    if (settled.Game == CasinoGame.Coinflip)
                    {
                        CoinClass(state, "coin", "flipping", false);
                        CoinText(state, "result", settled.Number == 0 ? "KOPF" : "ZAHL");
                        CoinClass(state, "coin", settled.Number == 0 ? "land-heads" : "land-tails", true);
                    }
                    else if (settled.Game == CasinoGame.Roulette)
                    {
                        RouletteClass(state, "wheel", "spinning", false);
                        RouletteClass(state, "pointer", "spinning", false);
                        var color = settled.Number == 0
                            ? "land-green"
                            : RouletteReds.Contains(settled.Number) ? "land-red" : "land-black";
                        RouletteClass(state, "wheel", color, true);
                        RouletteClass(state, "pointer", color, true);
                        for (var number = 0; number <= 36; number++)
                            RouletteClass(state, "wheel", "land-n" + number, settled.Number == number);
                    }
                }

                if (elapsed < TimeSpan.FromMilliseconds(4000))
                    continue;

                if (!state.LinesRevealed && state.Outcome is { Game: CasinoGame.Slots } slots)
                {
                    for (var line = 0; line < WinningLines.Length; line++)
                    {
                        var cells = WinningLines[line];
                        SlotsClass(state, "line" + line, "revealed", true);
                        SlotsClass(state, "line" + line, "active",
                            slots.Symbols[cells[0]] == slots.Symbols[cells[1]]
                            && slots.Symbols[cells[1]] == slots.Symbols[cells[2]]);
                    }
                    state.LinesRevealed = true;
                }

                state.Started = null;
                state.Win = state.Outcome!.Payout;
                state.Status = state.Outcome.Summary;
                if (state.Visible)
                    RenderActive(state);
            }
        }
    }

    private void SwitchGame(State state, CasinoGame game)
    {
        HideAllViews(state.Player.Id);
        state.Game = game;
        state.Stake = Math.Min(state.Stake, _stakeLimits.For(game));
        state.Status = game switch
        {
            CasinoGame.Slots => "5 Gewinnlinien · Einsatz wählen und spielen",
            CasinoGame.Coinflip => "Kopf oder Zahl wählen",
            CasinoGame.Roulette => "Zahl oder Außenwette wählen",
            _ => ""
        };
        RenderActive(state);
        _hud.Show(state.Player.Id, HudFor(game));
    }

    private void RenderActive(State state)
    {
        if (state.Game is null)
        {
            RenderLobby(state);
            return;
        }

        switch (state.Game.Value)
        {
            case CasinoGame.Slots:
                RenderSlots(state);
                break;
            case CasinoGame.Coinflip:
                RenderCoinflip(state);
                break;
            case CasinoGame.Roulette:
                RenderRoulette(state);
                break;
        }
    }

    private void RenderLobby(State state)
    {
        LobbyText(state, "profile", state.Player.Name);
        LobbyText(state, "account", Points(state.Balance.Available));
        LobbyText(state, "bank", Points(state.Balance.Allocated));
        LobbyText(state, "loadamount", Points(state.Load));
        LobbyText(state, "status", state.Status);
        LobbyClass(state, "root", "busy", state.Pending);
    }

    private void RenderSlots(State state)
    {
        SlotsText(state, "account", Points(state.Balance.Available));
        SlotsText(state, "bank", Points(state.Balance.Allocated));
        SlotsText(state, "stake", Points(state.Stake));
        SlotsText(state, "win", Points(state.Win));
        SlotsText(state, "status", state.Status);
        SlotsClass(state, "root", "busy", state.Pending || state.Started is not null);
    }

    private void RenderCoinflip(State state)
    {
        CoinText(state, "account", Points(state.Balance.Available));
        CoinText(state, "bank", Points(state.Balance.Allocated));
        CoinText(state, "stake", Points(state.Stake));
        CoinText(state, "win", Points(state.Win));
        CoinText(state, "choice", state.Coin == 0 ? "Kopf ausgewählt" : "Zahl ausgewählt");
        CoinText(state, "status", state.Status);
        CoinClass(state, "root", "busy", state.Pending || state.Started is not null);
    }

    private void RenderRoulette(State state)
    {
        RouletteText(state, "account", Points(state.Balance.Available));
        RouletteText(state, "bank", Points(state.Balance.Allocated));
        RouletteText(state, "stake", Points(state.Stake));
        RouletteText(state, "win", Points(state.Win));
        RouletteText(state, "numbervalue", state.Number.ToString(CultureInfo.InvariantCulture));
        RouletteText(state, "choice", "Wette: " + BetLabel(state.Bet));
        RouletteText(state, "status", state.Status);
        RouletteClass(state, "root", "busy", state.Pending || state.Started is not null);
    }

    private void SetOutcome(State state, CasinoOutcome outcome)
    {
        if (outcome.Game == CasinoGame.Slots)
        {
            for (var i = 0; i < 9; i++)
            {
                for (var symbol = 0; symbol < CasinoRules.Symbols.Length; symbol++)
                    SlotsClass(state, "symbol" + i, "symbol-" + CasinoRules.Symbols[symbol],
                        outcome.Symbols[i] == symbol);
            }
        }
        else if (outcome.Game == CasinoGame.Coinflip)
        {
            CoinText(state, "result", state.Started is null ? outcome.Number == 0 ? "KOPF" : "ZAHL" : "…");
            if (state.Started is null)
            {
                CoinClass(state, "coin", "land-heads", outcome.Number == 0);
                CoinClass(state, "coin", "land-tails", outcome.Number == 1);
            }
        }
        else if (outcome.Game == CasinoGame.Roulette)
        {
            RouletteText(state, "wheelresult", outcome.Number.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void ClearAnimationClasses(State state)
    {
        state.ReelStops = 0;
        state.ReelSettles = 0;
        state.LandingApplied = false;
        state.LinesRevealed = false;

        if (state.Game == CasinoGame.Slots)
        {
            for (var i = 0; i < 3; i++)
            {
                SlotsClass(state, "reel" + i, "rolling", false);
                SlotsClass(state, "reel" + i, "settling", false);
                SlotsClass(state, "reel" + i, "settled", false);
            }
            for (var line = 0; line < WinningLines.Length; line++)
            {
                SlotsClass(state, "line" + line, "revealed", false);
                SlotsClass(state, "line" + line, "active", false);
            }
        }
        else if (state.Game == CasinoGame.Coinflip)
        {
            foreach (var name in new[] { "flipping", "land-heads", "land-tails" })
                CoinClass(state, "coin", name, false);
        }
        else if (state.Game == CasinoGame.Roulette)
        {
            foreach (var name in new[] { "spinning", "land-red", "land-black", "land-green" })
            {
                RouletteClass(state, "wheel", name, false);
                RouletteClass(state, "pointer", name, false);
            }
            for (var number = 0; number <= 36; number++)
                RouletteClass(state, "wheel", "land-n" + number, false);
        }
    }

    private static string ActionFrom(CustomHudClickContext context)
    {
        var prefixes = context.HudId == LobbyHudId
            ? new[] { "casino_lobby_" }
            : context.HudId == SlotsHudId
                ? new[] { "casino_slots_" }
                : context.HudId == CoinflipHudId
                    ? new[] { "casino_coinflip_" }
                    : context.HudId == RouletteHudId
                        ? new[] { "casino_roulette_" }
                        : Array.Empty<string>();

        foreach (var prefix in prefixes)
            if (context.ButtonId.StartsWith(prefix, StringComparison.Ordinal))
                return context.ButtonId[prefix.Length..];
        return string.Empty;
    }

    private static CustomHudId HudFor(CasinoGame game)
        => game switch
        {
            CasinoGame.Slots => SlotsHudId,
            CasinoGame.Coinflip => CoinflipHudId,
            CasinoGame.Roulette => RouletteHudId,
            _ => LobbyHudId
        };

    private void HideAllViews(PlayerId player)
    {
        _hud.Hide(player, LobbyHudId);
        _hud.Hide(player, SlotsHudId);
        _hud.Hide(player, CoinflipHudId);
        _hud.Hide(player, RouletteHudId);
    }

    private static string Rules(CasinoGame? game)
        => game switch
        {
            CasinoGame.Slots => "Slots: 5 Linien · Einsatz/5 · 12/22/36/55/100/150/300/1000×.",
            CasinoGame.Coinflip => "Coinflip: 2× Auszahlung inklusive Einsatz.",
            CasinoGame.Roulette => "Roulette: Zahl 36× · Dutzend/Spalte 3× · andere Außenwetten 2×.",
            _ => "Wähle ein Spiel. Nur virtuelle €, keine Echtgeld-Auszahlung."
        };

    private static string BetLabel(int n)
        => n <= 36
            ? "Zahl " + n
            : new[]
            {
                "Rot", "Schwarz", "Gerade", "Ungerade", "1–18", "19–36",
                "1. Dutzend", "2. Dutzend", "3. Dutzend", "1. Spalte", "2. Spalte", "3. Spalte"
            }[n - 37];

    private static string Points(long n)
        => CasinoMoney.Format(n);

    private void LobbyText(State s, string id, string value)
        => _hud.SetText(s.Player.Id, LobbyHudId, "casino_lobby_" + id, value);
    private void LobbyClass(State s, string id, string name, bool value)
        => _hud.SetClass(s.Player.Id, LobbyHudId, "casino_lobby_" + id, name, value);

    private void SlotsText(State s, string id, string value)
        => _hud.SetText(s.Player.Id, SlotsHudId, "casino_slots_" + id, value);
    private void SlotsClass(State s, string id, string name, bool value)
        => _hud.SetClass(s.Player.Id, SlotsHudId, "casino_slots_" + id, name, value);

    private void CoinText(State s, string id, string value)
        => _hud.SetText(s.Player.Id, CoinflipHudId, "casino_coinflip_" + id, value);
    private void CoinClass(State s, string id, string name, bool value)
        => _hud.SetClass(s.Player.Id, CoinflipHudId, "casino_coinflip_" + id, name, value);

    private void RouletteText(State s, string id, string value)
        => _hud.SetText(s.Player.Id, RouletteHudId, "casino_roulette_" + id, value);
    private void RouletteClass(State s, string id, string name, bool value)
        => _hud.SetClass(s.Player.Id, RouletteHudId, "casino_roulette_" + id, name, value);

    private bool Current(State state)
        => !_disposed
           && _states.TryGetValue(state.Player.Id, out var current)
           && ReferenceEquals(state, current)
           && _players.TryGet(state.Player.Id, out var player)
           && player is { IsConnected: true }
           && player.SessionId == state.Player.SessionId;

    private void ForgetSession(PlayerSnapshot player)
    {
        lock (_gate)
        {
            if (_states.TryGetValue(player.Id, out var state)
                && state.Player.SessionId == player.SessionId)
            {
                _states.Remove(player.Id);
                HideAllViews(player.Id);
            }
        }
    }

    public void CloseForPlayer(PlayerId player)
    {
        lock (_gate)
        {
            if (_states.TryGetValue(player, out var state))
                state.Visible = false;
            HideAllViews(player);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            foreach (var player in _states.Keys.ToArray())
                HideAllViews(player);
            _states.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            Reset();
            _disposed = true;
            foreach (var owned in _owned)
                owned.Dispose();
        }
    }

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
        public int ReelSettles { get; set; }
        public bool LandingApplied { get; set; }
        public bool LinesRevealed { get; set; }
        public long Win { get; set; }
        public DateTimeOffset? Started { get; set; }
        public CasinoOutcome? Outcome { get; set; }
        public CasinoOperation? Unconfirmed { get; set; }
        public string Status { get; set; } = "Guthabenkonto laden oder Guthaben vom Profil auf den Automaten buchen.";
    }
}
