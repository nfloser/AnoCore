using AnoCore.Abstractions.Events;
using AnoCore.Abstractions.Players;

namespace AnoCore.Abstractions.Settings;

public sealed record PlayerSettingsResetEvent(
    PlayerId Player,
    int RemovedSettings) : IAnoEvent;
