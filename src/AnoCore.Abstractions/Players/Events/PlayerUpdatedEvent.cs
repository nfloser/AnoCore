using AnoCore.Abstractions.Events;

namespace AnoCore.Abstractions.Players.Events;

public sealed record PlayerUpdatedEvent(
    PlayerSnapshot Previous,
    PlayerSnapshot Current) : IAnoEvent;
