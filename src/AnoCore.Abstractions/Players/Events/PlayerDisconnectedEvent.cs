using AnoCore.Abstractions.Events;

namespace AnoCore.Abstractions.Players.Events;

public sealed record PlayerDisconnectedEvent(PlayerSnapshot Player) : IAnoEvent;
