using AnoCore.Abstractions.Players;
using AnoCore.Modules.Admin;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace AnoCore.Plugin.Moderation;

public sealed class CounterStrikeVoiceModerationTransport : IModerationVoiceTransport
{
    private readonly IPlayerRegistry _players;

    public CounterStrikeVoiceModerationTransport(IPlayerRegistry players)
        => _players = players ?? throw new ArgumentNullException(nameof(players));

    public bool TryGetOverride(
        PlayerSnapshot listener,
        PlayerSnapshot sender,
        out ModerationVoiceOverride value)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(sender);

        if (!TryResolve(listener, out var listenerController)
            || !TryResolve(sender, out var senderController))
        {
            value = default;
            return false;
        }

        try
        {
            value = listenerController!.GetListenOverride(senderController!) switch
            {
                ListenOverride.Default => ModerationVoiceOverride.Default,
                ListenOverride.Mute => ModerationVoiceOverride.Mute,
                ListenOverride.Hear => ModerationVoiceOverride.Hear,
                _ => throw new InvalidOperationException(
                    "CounterStrikeSharp returned an unknown voice listen override."),
            };
            return true;
        }
        catch (InvalidOperationException)
        {
            value = default;
            return false;
        }
    }

    public bool TrySetOverride(
        PlayerSnapshot listener,
        PlayerSnapshot sender,
        ModerationVoiceOverride value)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(sender);

        if (!TryResolve(listener, out var listenerController)
            || !TryResolve(sender, out var senderController))
        {
            return false;
        }

        var native = value switch
        {
            ModerationVoiceOverride.Default => ListenOverride.Default,
            ModerationVoiceOverride.Mute => ListenOverride.Mute,
            ModerationVoiceOverride.Hear => ListenOverride.Hear,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };

        try
        {
            listenerController!.SetListenOverride(senderController!, native);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool TryResolve(
        PlayerSnapshot snapshot,
        out CCSPlayerController? controller)
    {
        controller = null;

        if (!_players.TryGet(snapshot.Id, out var current)
            || current is null
            || !current.IsConnected
            || current.SessionId != snapshot.SessionId)
        {
            return false;
        }

        controller = Utilities.GetPlayerFromSteamId64(snapshot.Id.SteamId64);
        return controller is
        {
            IsValid: true,
            IsBot: false,
            IsHLTV: false,
        } && controller.SteamID == snapshot.Id.SteamId64;
    }
}
