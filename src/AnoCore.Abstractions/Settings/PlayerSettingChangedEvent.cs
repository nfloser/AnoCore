using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Settings;

public enum PlayerSettingChangeKind
{
    Set,
    Reset,
}

public sealed record PlayerSettingChangedEvent(
    PlayerId Player,
    string SettingName,
    PlayerSettingChangeKind Kind) : IAnoEvent;
