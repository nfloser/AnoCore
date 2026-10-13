using System.Globalization;
using AnoCore.Abstractions.Commands;
using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Hud;
using AnoCore.Abstractions.Modules;
using AnoCore.Abstractions.Permissions;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Players.Events;

namespace AnoCore.Modules.Arcade;

/// <summary>Native animation acceptance probe. Credits are isolated, ephemeral test data.</summary>
public sealed class ArcadeProbePresenter : IDisposable
{
    public static readonly CustomHudId HudId = new("ano.arcade-probe");
    private static readonly ModuleId Owner = new("ano.arcade-probe");
    private static readonly PermissionId Permission = new("ano.casino.test");
    private static readonly string[] Buttons = ["close", "home", "slots", "coinflip", "roulette", "cases",
        "load", "return", "minus", "plus", "spin", "refresh"];
    private static readonly TimeSpan SpinDuration = TimeSpan.FromSeconds(4);
    private readonly object _gate = new();
    private readonly ICustomHudService _hud;
    private readonly IPlayerRegistry _players;
    private readonly IPermissionEvaluator _permissions;
    private readonly TimeProvider _clock;
    private readonly Action<PlayerId>? _beforeOpen;
    private readonly Dictionary<PlayerId, State> _states = [];
    private readonly List<IDisposable> _registrations = [];
    private bool _disposed;

    public ArcadeProbePresenter(ICustomHudService hud, IPlayerRegistry players,
        IPermissionEvaluator permissions, IAnoCommandRegistry commands, IAnoEventBus events,
        TimeProvider? clock = null, Action<PlayerId>? beforeOpen = null)
    {
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _clock = clock ?? TimeProvider.System;
        _beforeOpen = beforeOpen;
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(events);
        try
        {
            _registrations.Add(hud.Register(Owner, new CustomHudDefinition(HudId,
                "panorama/layout/custom_game/anocore/ano_arcade.xml", "ano_arcade_root",
                Buttons.Select(x => "ano_arcade_" + x).ToArray(), captureInput: true), ClickAsync));
            _registrations.Add(commands.Register(Owner,
                new CommandDescriptor("anoarcadetest", "Open the isolated arcade animation test.", Permission), OpenAsync));
            _registrations.Add(events.Subscribe<PlayerDisconnectedEvent>((value, _) =>
            {
                ForgetSession(value.Player);
                return ValueTask.CompletedTask;
            }));
            _registrations.Add(events.Subscribe<PlayerReconnectedEvent>((value, _) =>
            {
                ForgetSession(value.Previous);
                return ValueTask.CompletedTask;
            }));
        }
        catch
        {
            foreach (var registration in _registrations) registration.Dispose();
            throw;
        }
    }

    private ValueTask<CommandResult> OpenAsync(CommandContext context)
    {
        lock (_gate)
        {
            if (_disposed || context.Caller is null || !_players.TryGet(context.Caller, out var player)
                || player is not { IsConnected: true })
                return ValueTask.FromResult(CommandResult.Fail(CommandFailureReason.Forbidden,
                    "Ein verbundener Spieler ist erforderlich."));
            if (!_states.TryGetValue(player.Id, out var state) || state.Player.SessionId != player.SessionId)
            {
                state = new State(player);
                _states[player.Id] = state;
            }
            _beforeOpen?.Invoke(player.Id);
            state.Visible = true;
            Render(state);
            if (!_hud.Show(player.Id, HudId))
            {
                state.Visible = false;
                return ValueTask.FromResult(CommandResult.Fail(CommandFailureReason.Forbidden,
                    "Casino-Oberfläche konnte nicht geöffnet werden."));
            }
            return ValueTask.FromResult(CommandResult.Ok("[ANO] Arcade-Test geöffnet. Nur flüchtiges Testguthaben."));
        }
    }

    private async ValueTask ClickAsync(CustomHudClickContext context)
    {
        if (context.HudId != HudId) return;
        State captured;
        lock (_gate)
        {
            if (_disposed || !_states.TryGetValue(context.PlayerId, out captured!) || !captured.Visible) return;
        }
        // A reconnect or a replaced probe must win over delayed permission evaluation.
        var allowed = await _permissions.HasPermissionAsync(context.PlayerId, Permission,
            context.CancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed || !_states.TryGetValue(context.PlayerId, out var state)
                || !ReferenceEquals(state, captured) || !state.Visible) return;
            if (!Current(state) || !allowed) { Forget(context.PlayerId); return; }
            if (context.ButtonId == "ano_arcade_close")
            {
                state.Visible = false;
                _hud.Hide(context.PlayerId, HudId);
                return;
            }
            if (!Buttons.Contains(context.ButtonId.Replace("ano_arcade_", "", StringComparison.Ordinal), StringComparer.Ordinal)) return;
            if (context.ButtonId == "ano_arcade_refresh")
            {
                state.Status = "Text aktualisiert – Walzen sollen weiterlaufen";
                Text(state, "status", state.Status);
                return;
            }
            if (state.StartedAt is not null) return;
            switch (context.ButtonId)
            {
                case "ano_arcade_home": state.Slots = false; break;
                case "ano_arcade_slots": state.Slots = true; break;
                case "ano_arcade_coinflip":
                case "ano_arcade_roulette":
                case "ano_arcade_cases": state.Status = "Geplant – in diesem Animationstest noch nicht spielbar"; break;
                case "ano_arcade_load":
                    var amount = Math.Min(500, state.Account);
                    state.Account -= amount;
                    state.Bank += amount;
                    state.Status = amount == 0 ? "Kein Testguthaben verfügbar" : "Testguthaben aufgeladen";
                    break;
                case "ano_arcade_return":
                    state.Account += state.Bank;
                    state.Bank = 0;
                    state.Status = "Testguthaben zurückgebucht";
                    break;
                case "ano_arcade_minus": state.Stake = Math.Max(50, state.Stake - 50); break;
                case "ano_arcade_plus": state.Stake = Math.Min(500, state.Stake + 50); break;
                case "ano_arcade_spin" when state.Slots:
                    if (state.Bank < state.Stake) { state.Status = "Bitte zuerst Testguthaben aufladen"; break; }
                    state.Bank -= state.Stake;
                    state.Win = 0;
                    state.StartedAt = _clock.GetUtcNow();
                    state.Refreshed = false;
                    state.Spin++;
                    state.Status = "Walzen drehen – Ergebnis ist eine feste Testsequenz";
                    for (var i = 0; i < 3; i++)
                    {
                        _hud.SetClass(state.Player.Id, HudId, $"ano_arcade_reel_{i}", "spin-a", state.Spin % 2 == 1);
                        _hud.SetClass(state.Player.Id, HudId, $"ano_arcade_reel_{i}", "spin-b", state.Spin % 2 == 0);
                    }
                    break;
            }
            Render(state);
        }
    }

    /// <summary>Bounded status/stop updates only; never drives individual animation frames.</summary>
    public void Tick()
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var state in _states.Values.ToArray())
            {
                if (!Current(state)) { Forget(state.Player.Id); continue; }
                if (state.StartedAt is not { } start) continue;
                var elapsed = _clock.GetUtcNow() - start;
                if (!state.Refreshed && elapsed >= TimeSpan.FromSeconds(1))
                {
                    state.Refreshed = true;
                    state.Status = "Textupdate während der Animation – auf Ruckler prüfen";
                    Text(state, "status", state.Status);
                }
                for (var i = 0; i < 3; i++)
                {
                    if (elapsed >= TimeSpan.FromMilliseconds(2600 + i * 500)) StopReel(state, i);
                }
                if (elapsed < SpinDuration) continue;
                state.StartedAt = null;
                // Alternating fixed win/no-win sequence exercises both displays. Not game mathematics.
                state.Win = state.Spin % 2 == 0 ? state.Stake * 2 : 0;
                state.Bank += state.Win;
                state.Status = state.Win == 0 ? "Testspin beendet – kein Gewinn" : "Testspin beendet – Testgewinn";
                Render(state);
            }
        }
    }

    private void StopReel(State state, int reel)
    {
        if (state.Stopped[reel] == state.Spin) return;
        state.Stopped[reel] = state.Spin;
        var panel = $"ano_arcade_reel_{reel}";
        _hud.SetClass(state.Player.Id, HudId, panel, "spin-a", false);
        _hud.SetClass(state.Player.Id, HudId, panel, "spin-b", false);
        // Each middle row is cherry for the even winning sequence; odd has different fruits.
        for (var offset = 0; offset < 3; offset++)
            _hud.SetClass(state.Player.Id, HudId, panel, $"stop-{offset}",
                offset == (state.Spin % 2 == 0 ? 0 : reel));
    }

    private void Render(State state)
    {
        _hud.SetClass(state.Player.Id, HudId, "ano_arcade_root", "view-slots", state.Slots);
        _hud.SetClass(state.Player.Id, HudId, "ano_arcade_root", "busy", state.StartedAt is not null);
        Text(state, "account", Points(state.Account));
        Text(state, "profile", state.Player.Name);
        Text(state, "bank", Points(state.Bank));
        Text(state, "stake", Points(state.Stake));
        Text(state, "win", Points(state.Win));
        Text(state, "status", state.Status);
    }

    private static string Points(int value) => value.ToString("N0", CultureInfo.GetCultureInfo("de-DE")) + " Test-AP";
    private void Text(State state, string panel, string value) => _hud.SetText(state.Player.Id, HudId, "ano_arcade_" + panel, value);
    private bool Current(State state) => _players.TryGet(state.Player.Id, out var player)
        && player is { IsConnected: true } && player.SessionId == state.Player.SessionId;

    private void ForgetSession(PlayerSnapshot player)
    {
        lock (_gate)
        {
            if (_states.TryGetValue(player.Id, out var state)
                && state.Player.SessionId == player.SessionId) Forget(player.Id);
        }
    }

    private void Forget(PlayerId player)
    {
        lock (_gate)
        {
            if (_states.Remove(player)) _hud.Hide(player, HudId);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _hud.HideAll(HudId);
            _states.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Reset();
            foreach (var registration in _registrations) registration.Dispose();
            _registrations.Clear();
        }
    }

    private sealed class State(PlayerSnapshot player)
    {
        public PlayerSnapshot Player { get; } = player;
        public bool Visible { get; set; }
        public bool Slots { get; set; }
        public int Account { get; set; } = 2750;
        public int Bank { get; set; }
        public int Stake { get; set; } = 100;
        public int Win { get; set; }
        public long Spin { get; set; }
        public long[] Stopped { get; } = new long[3];
        public DateTimeOffset? StartedAt { get; set; }
        public bool Refreshed { get; set; }
        public string Status { get; set; } = "Animationstest – kein dauerhaftes AP-Konto";
    }
}
