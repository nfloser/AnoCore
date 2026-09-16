using AnoCore.Abstractions.Events;

namespace AnoCore.Abstractions.Players.Events;

public sealed record PlayerConnectedEvent(PlayerSnapshot Player) : IAnoEvent;
