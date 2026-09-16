using AnoCore.Abstractions.Events;

namespace AnoCore.Abstractions.Players.Events;

public sealed record PlayerReconnectedEvent(
    PlayerSnapshot Previous,
    PlayerSnapshot Current) : IAnoEvent;
